using System;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace Prometheus.Client.Services;

/// <summary>Converts composed D3D11 frames to NV12 and submits them to the NVIDIA hardware MFT.</summary>
internal sealed class GpuVideoWriter : IDisposable
{
    // MFVideoFormat_HEVC, from the Windows SDK's mfapi.h.
    private static readonly Guid HevcSubtype = new("43564548-0000-0010-8000-00AA00389B71");
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly ID3D11VideoContext1 _videoContext1;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessor _processor;
    private readonly IMFDXGIDeviceManager _manager;
    private readonly IMFSinkWriter _writer;
    private readonly int _stream;
    private readonly int _width;
    private readonly int _height;
    private readonly Prometheus.Core.Primitives.Rational _fps;
    private ID3D11VideoProcessorInputView? _inputView;
    private IntPtr _inputTexture;
    private bool _finished;
    private bool _mfStarted;

    public GpuVideoWriter(ID3D11Device device, ID3D11DeviceContext context,
        string path, int width, int height, Prometheus.Core.Primitives.Rational fps, int bitrate, bool hevc)
    {
        ValidateDimensions(width, height);
        _device = device;
        _context = context;
        _width = width;
        _height = height;
        _fps = fps;
        try
        {
            MFStartup().CheckError();
            _mfStarted = true;
            _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
            _videoContext = context.QueryInterface<ID3D11VideoContext>();
            _videoContext1 = context.QueryInterface<ID3D11VideoContext1>();
            var rate = new Rational(checked((uint)fps.N), checked((uint)fps.D));
            var content = new VideoProcessorContentDescription
            {
                InputFrameFormat = VideoFrameFormat.Progressive,
                InputFrameRate = rate,
                InputWidth = (uint)width,
                InputHeight = (uint)height,
                OutputFrameRate = rate,
                OutputWidth = (uint)width,
                OutputHeight = (uint)height,
                Usage = VideoUsage.PlaybackNormal
            };
            _videoDevice.CreateVideoProcessorEnumerator(ref content, out _enumerator).CheckError();
            _videoDevice.CreateVideoProcessor(_enumerator, 0, out _processor).CheckError();
            _videoContext1.VideoProcessorSetStreamColorSpace1(_processor, 0,
                ColorSpaceType.RgbFullG22NoneP709);
            _videoContext1.VideoProcessorSetOutputColorSpace1(_processor,
                ColorSpaceType.YcbcrStudioG22LeftP709);
            _manager = MFCreateDXGIDeviceManager();
            _manager.ResetDevice(device).CheckError();
            using var attributes = MFCreateAttributes(4);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u).CheckError();
            attributes.Set(SinkWriterAttributeKeys.D3DManager, _manager).CheckError();
            attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Mpeg4).CheckError();
            _writer = MFCreateSinkWriterFromURL(path, null!, attributes);
            using var output = MakeType(hevc ? HevcSubtype : VideoFormatGuids.H264, width, height, rate);
            output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate).CheckError();
            _stream = _writer.AddStream(output);
            using var input = MakeType(VideoFormatGuids.NV12, width, height, rate);
            _writer.SetInputMediaType(_stream, input, null!);
            using var extended = _writer.QueryInterface<IMFSinkWriterEx>();
            var nvidia = false;
            var names = new System.Collections.Generic.List<string>();
            for (var i = 0; i < 8; i++)
            {
                try
                {
                    extended.GetTransformForStream(_stream, i, out _, out var transform);
                    using (transform)
                    {
                        var name = transform.Attributes.GetString(TransformAttributeKeys.MftFriendlyNameAttribute);
                        uint aware = 0;
                        try { aware = transform.Attributes.GetUInt32(TransformAttributeKeys.D3D11Aware); }
                        catch (SharpGenException) { }
                        names.Add(name + " D3D11=" + aware);
                        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) nvidia = true;
                    }
                }
                catch (SharpGenException ex) { names.Add(ex.Message); break; }
            }
            if (!nvidia) throw new NotSupportedException("Codificador de vídeo seleccionado: " + string.Join("; ", names));
            _writer.BeginWriting();
        }
        catch
        {
            _writer?.Dispose();
            _manager?.Dispose();
            _processor?.Dispose();
            _enumerator?.Dispose();
            _videoContext1?.Dispose();
            _videoContext?.Dispose();
            _videoDevice?.Dispose();
            if (_mfStarted) MFShutdown();
            throw;
        }
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width < 256 || height < 256 || (width & 1) != 0 || (height & 1) != 0)
            throw new NotSupportedException(
                "La exportación con la GPU NVIDIA de este equipo requiere al menos 256 × 256 píxeles y dimensiones pares.");
    }

    public void Write(ID3D11Texture2D bgra, int frameIndex)
    {
        using var nv12 = _device.CreateTexture2D(new Texture2DDescription(Format.NV12,
            (uint)_width, (uint)_height, 1, 1, BindFlags.RenderTarget | BindFlags.VideoEncoder));
        var sourceDesc = new VideoProcessorInputViewDescription
            { ViewDimension = VideoProcessorInputViewDimension.Texture2D };
        var outputDesc = new VideoProcessorOutputViewDescription
            { ViewDimension = VideoProcessorOutputViewDimension.Texture2D };
        if (_inputView == null || _inputTexture != bgra.NativePointer)
        {
            _inputView?.Dispose();
            _videoDevice.CreateVideoProcessorInputView(bgra, _enumerator, sourceDesc, out _inputView).CheckError();
            _inputTexture = bgra.NativePointer;
        }
        _videoDevice.CreateVideoProcessorOutputView(nv12, _enumerator, outputDesc, out var outputView).CheckError();
        using (outputView)
        {
            var stream = new VideoProcessorStream { Enable = true, InputSurface = _inputView };
            _videoContext.VideoProcessorBlt(_processor, outputView, 0, 1, [stream]).CheckError();
        }
        _context.Flush();
        using var buffer = MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, nv12, 0, false);
        buffer.CurrentLength = buffer.MaxLength;
        using var sample = MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = FrameTimestamp(frameIndex, _fps);
        sample.SampleDuration = FrameTimestamp(frameIndex + 1, _fps) - sample.SampleTime;
        _writer.WriteSample(_stream, sample);
    }

    internal static long FrameTimestamp(int index, Prometheus.Core.Primitives.Rational fps)
    {
        if (index < 0 || fps.N <= 0 || fps.D <= 0) throw new ArgumentOutOfRangeException(nameof(index));
        var numerator = (Int128)index * 10_000_000 * fps.D;
        return checked((long)((numerator + fps.N / 2) / fps.N));
    }

    public void Finish()
    {
        if (_finished) return;
        _writer.NotifyEndOfSegment(_stream);
        _writer.Finalize();
        _finished = true;
    }

    private static IMFMediaType MakeType(Guid subtype, int width, int height, Rational rate)
    {
        var type = MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
        type.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive).CheckError();
        type.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)(uint)width << 32) | (uint)height).CheckError();
        type.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)rate.Numerator << 32) | rate.Denominator).CheckError();
        type.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((ulong)1 << 32) | 1).CheckError();
        type.Set(MediaTypeAttributeKeys.VideoPrimaries, (uint)VideoPrimaries.Bt709).CheckError();
        type.Set(MediaTypeAttributeKeys.TransferFunction, (uint)VideoTransferFunction.Func709).CheckError();
        type.Set(MediaTypeAttributeKeys.YuvMatrix, (uint)VideoTransferMatrix.Bt709).CheckError();
        type.Set(MediaTypeAttributeKeys.VideoNominalRange, (uint)NominalRange.Range16_235).CheckError();
        return type;
    }

    public void Dispose()
    {
        _writer.Dispose();
        _inputView?.Dispose();
        _manager.Dispose();
        _processor.Dispose();
        _enumerator.Dispose();
        _videoContext.Dispose();
        _videoContext1.Dispose();
        _videoDevice.Dispose();
        if (_mfStarted) MFShutdown();
    }
}
