using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Prometheus.Client.Docking;
using Prometheus.Client.Panels;
using Prometheus.Client.Services;
using Prometheus.Client.Mcp;
using Prometheus.Common.Docking;
using Prometheus.Common.Persistence;
using Prometheus.Core.Timeline;
using Prometheus.Core.Model;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.Json;

namespace Prometheus.Client.Views;

public partial class MainWindow : Window
{
    private readonly LayoutPersistenceService _persistenceService = new();
    private readonly LayoutPersistenceService _customLayoutService = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Prometheus", "layout-v1-personalizado.json"));
    private WorkspaceLayout _currentLayout = FactoryPresets.CreateEquilibrado();
    private WorkspaceLayout? _savedCustomLayout;
    private readonly ProjectController _projectController;
    private GlobalShelfHotkey? _shelfHotkey;
    private McpLiveHost? _mcpHost;
    private bool _spacePressed;
    private bool _closeApproved;
    private bool _closePromptOpen;
    private bool _closed;
    private bool _starting = true;

    public string? StartupProjectPath { get; init; }

    public MainWindow()
    {
        InitializeComponent();
        BuildVersionLabel.Text = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);

        _projectController = new ProjectController(this);
        AddHandler(KeyDownEvent, TransportKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, TransportKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, RestoreTransportFocus, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => _spacePressed = false;
        _projectController.StateChanged += UpdateProjectUI;
        _projectController.StatusMessageRequested += msg => StatusBarStatus.Text = msg;

        DockContainer.FloatPanelRequested += HandleFloatPanel;
        DockContainer.MinimizePanelRequested += HandleMinimizePanel;
        DockContainer.ClosePanelRequested += HandleClosePanel;
        DockContainer.DroppedAsFloatingRequested += HandleDroppedAsFloating;
        DockContainer.PanelDocked += HandlePanelDocked;
        DockContainer.LayoutModified += HandleLayoutModified;
        DockContainer.ReplacePanelRequested += HandleReplacePanel;

        ShelfNotch.RestoreMinimizedAsFloatingRequested += HandleRestoreMinimizedAsFloating;
        ShelfNotch.PanelDirectActionRequested += HandlePanelDirectAction;

        SetupProjectMenu();
        SetupSettingsMenu();
        SetupLayoutsMenu();
        SetupMcpButton();
        SetupNotificationsButton();
        ExportTopButton.Click += async (_, _) => await ExportActiveSequenceAsync();
        TimelinePanelView.ShelfShortcutChanged += UpdateShelfHotkey;

        Loaded += async (_, _) =>
        {
            try
            {
                InitializeWorkspace();
                UpdateProjectUI();
                var shortcut = TimelinePanelView.GetShelfShortcut();
                UpdateShelfHotkey(shortcut.Key, shortcut.Modifiers);
                if (System.IO.File.Exists(StartupProjectPath))
                    await _projectController.OpenProjectFromPathAsync(StartupProjectPath);
                else
                    await _projectController.OfferRecoveryAsync();
            }
            catch (Exception error) { _projectController.ReportStatus("No se pudo completar el inicio: " + error.Message); }
            finally
            {
                _starting = false;
                if (!_closed) SetMcpActive(true);
            }
        };
        Closing += OnMainWindowClosing;
    }

    private void SetupMcpButton()
    {
        McpTopButton.Click += (_, _) => McpTopButton.ContextMenu?.Open(McpTopButton);
        McpToggleItem.Click += async (_, _) =>
        {
            McpTopButton.IsEnabled = false;
            try { await SetMcpActiveAsync(_mcpHost == null); }
            finally { McpTopButton.IsEnabled = true; }
        };
        McpCopyConfigItem.Click += async (_, _) =>
        {
            var bridge = System.IO.Path.Combine(AppContext.BaseDirectory, "McpBridge", "Prometheus.McpBridge.exe");
            if (!System.IO.File.Exists(bridge)) bridge = System.IO.Path.Combine(AppContext.BaseDirectory, "Prometheus.McpBridge.exe");
            var config = JsonSerializer.Serialize(new { mcpServers = new System.Collections.Generic.Dictionary<string, object>
                { ["prometheus-v3-2"] = new { command = bridge } } }, new JsonSerializerOptions { WriteIndented = true });
            if (Clipboard != null) await Clipboard.SetTextAsync(config);
            StatusBarStatus.Text = "Configuración MCP copiada";
        };
        UpdateMcpButton();
    }

    private void SetMcpActive(bool active)
    {
        if (active && !_starting && !_closed && _mcpHost == null)
        {
            var host = new McpLiveHost(_projectController);
            host.ReviewRequested += ShowMcpReview;
            host.LogMessage += message =>
            {
                if (PanelRegistry.GetExistingPanelContent("instructions") is InstructionsPanelView panel)
                    panel.AppendLog(message);
            };
            var instructions = PanelRegistry.GetExistingPanelContent("instructions") as InstructionsPanelView
                ?? PanelRegistry.GetOrCreatePanelContent("instructions") as InstructionsPanelView;
            instructions?.Configure(host.EvaluateLocalAsync, host.ApplyLocal);
            host.Start();
            _mcpHost = host;
        }
        UpdateMcpButton();
    }

    private async System.Threading.Tasks.Task SetMcpActiveAsync(bool active)
    {
        if (active) SetMcpActive(true);
        else if (_mcpHost != null)
        {
            var host = _mcpHost;
            await host.StopAsync();
            _mcpHost = null;
            UpdateMcpButton();
        }
    }

    private void UpdateMcpButton()
    {
        if (McpStatusDot == null) return;
        var active = _mcpHost != null;
        McpStatusDot.Foreground = new SolidColorBrush(Color.Parse(active ? "#69D68B" : "#808080"));
        McpToggleItem.Header = active ? "Desactivar MCP" : "Activar MCP";
        ToolTip.SetTip(McpTopButton, active ? "MCP activo" : "MCP apagado");
    }

    private async void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_projectController.AnalysisWork.Active is { } analysis)
        {
            e.Cancel = true;
            analysis.Cancel();
            return;
        }
        if (!_closeApproved && _projectController.IsDirty)
        {
            e.Cancel = true;
            if (_closePromptOpen) return;
            _closePromptOpen = true;
            try
            {
                if (await _projectController.ConfirmDiscardChangesAsync("salir"))
                {
                    _closeApproved = true;
                    Close();
                }
            }
            finally { _closePromptOpen = false; }
            return;
        }
        _closed = true;
        TimelinePanelView.ShelfShortcutChanged -= UpdateShelfHotkey;
        _shelfHotkey?.Dispose();
        _mcpHost?.Dispose();
        _projectController.CloseHistory();
        SaveLayoutState();
    }

    private void UpdateShelfHotkey(Key key, KeyModifiers modifiers)
    {
        _shelfHotkey?.Dispose();
        _shelfHotkey = null;
        if (key == Key.None) return;
        try { _shelfHotkey = new GlobalShelfHotkey(key, modifiers, CaptureSelectionToShelf, _projectController.ReportStatus); }
        catch (ArgumentException ex) { _projectController.ReportStatus(ex.Message); }
    }

    private async void CaptureSelectionToShelf()
    {
        if (_projectController.AnalysisWork.Active != null) return;
        var content = await AdaptiveClipboard.ReadAsync(this);
        if (_projectController.AnalysisWork.Active != null) return;
        if (content.Error != null) { _projectController.ReportStatus(content.Error); return; }
        var text = content.Files.Length > 0 ? string.Join(Environment.NewLine, content.Files) : content.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (PanelRegistry.GetExistingPanelContent("media_pool") is MediaPoolPanelView media && media.IsVisible)
                media.ShowLinkShelf(true, text);
            else LinkShelfWindow.ShowFor(this, _projectController, text);
        }
        else _projectController.ReportStatus("La selección no contiene texto para la bandeja");
    }

    private void SetupProjectMenu()
    {
        ProjectMenuButton.Click += (_, _) =>
        {
            if (ProjectMenuButton.ContextMenu != null)
            {
                UpdateRecentProjectsMenu();
                ProjectMenuButton.ContextMenu.Open(ProjectMenuButton);
            }
        };

        MenuNewProject.Click += async (_, _) => await _projectController.NewProjectAsync();
        MenuOpenProject.Click += async (_, _) => await _projectController.OpenProjectAsync();
        MenuCloseProject.Click += async (_, _) => await _projectController.CloseProjectAsync();
        MenuRecoverProject.Click += async (_, _) => await _projectController.OfferRecoveryAsync();
        MenuSaveProject.Click += async (_, _) => await _projectController.SaveProjectAsync();
        MenuSaveProjectAs.Click += async (_, _) => await _projectController.SaveProjectAsync(saveAs: true);
        MenuExportProject.Click += async (_, _) => await ExportActiveSequenceAsync();
        MenuUndo.Click += (_, _) => _projectController.Undo();
        MenuRedo.Click += (_, _) => _projectController.Redo();
    }

    private void SetupSettingsMenu()
    {
        SettingsMenuButton.Click += (_, _) => SettingsMenuButton.ContextMenu?.Open(SettingsMenuButton);
        MenuKeyboardShortcuts.Click += (_, _) =>
        {
            var timeline = PanelRegistry.GetExistingPanelContent("timeline") as TimelinePanelView
                ?? PanelRegistry.CreatePanelContent("timeline") as TimelinePanelView;
            timeline?.OpenShortcutSettings(this);
        };
        MenuWebDownloads.Click += async (_, _) => await OpenWebDownloadsAsync();
        MenuPreviewSettings.Click += async (_, _) => await OpenPreviewSettingsAsync();
        MenuProcessingSettings.Click += async (_, _) =>
        {
            await ProcessingService.InitializeAsync();
            await new ProcessingSettingsDialog().ShowDialog(this);
        };
        MenuAbout.Click += async (_, _) => await new AboutWindow().ShowDialog(this);
    }

    private void UpdateProjectUI()
    {
        Title = _projectController.WindowTitle;
        var sequence = _projectController.CurrentProject.ActiveSequence;
        StatusBarVideoFormat.Text = sequence == null ? "Sin montaje activo" :
            $"{sequence.Width}x{sequence.Height} @ {sequence.Fps.Seconds:0.00} FPS";
        StatusBarAudioFormat.Text = sequence == null ? "" :
            $"Audio {sequence.MixSampleRate} Hz {(sequence.MixChannels == 2 ? "Estéreo" : sequence.MixChannels + " canales")}";
        StatusBarProject.Text = $"Proyecto: {_projectController.CurrentProject.Name}{(_projectController.IsDirty ? "*" : "")} (Rev {_projectController.CurrentProject.Revision})";
    }

    private void SaveSequenceView(Project project)
    {
        var view = _projectController.CaptureSequenceView?.Invoke();
        var current = project.ActiveSequence;
        if (view == null || current == null) return;
        current.ViewPlayheadSeconds = view.Value.Playhead;
        current.ViewOffsetSeconds = view.Value.Offset;
        current.ViewZoomPixelsPerSecond = view.Value.Zoom;
    }

    internal void CreateSequence()
    {
        _projectController.Transport.Pause();
        _projectController.UndoRedo.Commit(p =>
        {
            SaveSequenceView(p);
            var number = 1;
            while (p.Sequences.Any(s => s.Name.Equals($"Montaje {number}", StringComparison.OrdinalIgnoreCase))) number++;
            SequenceOperations.Create(p, $"Montaje {number}", p.ActiveSequence);
        }, "Crear secuencia");
    }

    internal void SwitchSequence(string id)
    {
        if (_projectController.CurrentProject.ActiveSequenceId == id) return;
        _projectController.Transport.Pause();
        _projectController.UndoRedo.Commit(p =>
        {
            SaveSequenceView(p);
            p.ActiveSequenceId = id;
        }, "Cambiar secuencia");
    }

    internal void DuplicateSequence(string id)
    {
        _projectController.Transport.Pause();
        _projectController.UndoRedo.Commit(p =>
        {
            SaveSequenceView(p);
            SequenceOperations.Duplicate(p, id);
        }, "Duplicar secuencia");
    }

    internal void DeleteSequence(string id)
    {
        try
        {
            _projectController.Transport.Pause();
            _projectController.UndoRedo.Commit(p => SequenceOperations.Delete(p, id), "Eliminar secuencia");
        }
        catch (Exception ex) { _projectController.ReportStatus(ex.Message); }
    }

    internal async System.Threading.Tasks.Task RenameSequenceAsync(string id)
    {
        var sequence = _projectController.CurrentProject.Sequences.FirstOrDefault(s => s.Id == id);
        if (sequence == null) return;
        var dialog = new Window { Title = "Renombrar montaje", Width = 340, Height = 130, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var input = new TextBox { Text = sequence.Name, Margin = new Thickness(10) };
        var accept = new Button { Content = "Guardar", MinWidth = 75 };
        var cancel = new Button { Content = "Cancelar", MinWidth = 75 };
        accept.Click += (_, _) => dialog.Close(input.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(10, 0, 10, 10) };
        actions.Children.Add(cancel);
        actions.Children.Add(accept);
        var body = new StackPanel();
        body.Children.Add(input);
        body.Children.Add(actions);
        dialog.Content = body;
        var name = await dialog.ShowDialog<string?>(this);
        if (name == null) return;
        try { _projectController.UndoRedo.Commit(p => SequenceOperations.Rename(p, id, name), "Renombrar secuencia"); }
        catch (Exception ex) { _projectController.ReportStatus(ex.Message); }
    }

    private void RestoreTransportFocus(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            !SpaceShortcutPolicy.IsTextInput(FocusManager?.GetFocusedElement()) || e.Source is not Visual visual)
            return;
        // Blank panel surfaces can leave the last text editor focused. Preserve
        // normal pointer focus on actual controls and leave popup roots alone.
        if (!ReferenceEquals(TopLevel.GetTopLevel(visual), this)) return;
        for (var current = visual; current != null && current != this; current = current.GetVisualParent())
            if (current is InputElement { Focusable: true }) return;
        Focus(NavigationMethod.Pointer);
    }

    private void TransportKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control &&
            !SpaceShortcutPolicy.IsTextInput(e.Source) && e.Source is not ComboBox)
        {
            if (PanelRegistry.GetExistingPanelContent("timeline") is TimelinePanelView timeline)
                timeline.PasteContextual();
            else _projectController.ReportStatus("Abre la línea de tiempo para pegar en el cabezal");
            e.Handled = true;
            return;
        }
        if (!SpaceShortcutPolicy.ShouldToggle(e.Key, e.KeyModifiers, e.Source)) return;
        if (!_spacePressed) _projectController.Transport.TogglePlayback();
        _spacePressed = true;
        e.Handled = true;
    }

    private void TransportKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (_spacePressed) e.Handled = true;
        _spacePressed = false;
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (isCtrl && !isShift && e.Key == Key.N)
        {
            await _projectController.NewProjectAsync();
            e.Handled = true;
        }
        else if (isCtrl && !isShift && e.Key == Key.O)
        {
            await _projectController.OpenProjectAsync();
            e.Handled = true;
        }
        else if (isCtrl && !isShift && e.Key == Key.S)
        {
            await _projectController.SaveProjectAsync();
            e.Handled = true;
        }
        else if (isCtrl && isShift && e.Key == Key.S)
        {
            await _projectController.SaveProjectAsync(saveAs: true);
            e.Handled = true;
        }
        else if (isCtrl && !isShift && e.Key == Key.Z && !SpaceShortcutPolicy.IsTextInput(e.Source))
        {
            _projectController.Undo();
            e.Handled = true;
        }
        else if (isCtrl && isShift && e.Key == Key.Z && !SpaceShortcutPolicy.IsTextInput(e.Source))
        {
            _projectController.Redo();
            e.Handled = true;
        }
    }

    private void SetupLayoutsMenu()
    {
        LayoutsGridButton.Click += (_, _) =>
        {
            if (LayoutsGridButton.ContextMenu != null)
            {
                LayoutsGridButton.ContextMenu.Open(LayoutsGridButton);
            }
        };

        PresetEquilibradoItem.Click += (_, _) => ApplyPreset(FactoryPresets.PresetEquilibrado);
        PresetMontajeItem.Click += (_, _) => ApplyPreset(FactoryPresets.PresetMontaje);
        PresetBRollItem.Click += (_, _) => ApplyPreset(FactoryPresets.PresetBRoll);
        PresetRevisionItem.Click += (_, _) => ApplyPreset(FactoryPresets.PresetRevision);
        PresetEdicionCompletaItem.Click += (_, _) => ApplyPreset(FactoryPresets.PresetEdicionCompleta);
        PresetPersonalizadoItem.Click += (_, _) => ApplyPreset(FactoryPresets.PresetPersonalizado);
        PresetResetItem.Click += (_, _) => ResetCurrentPreset();
    }

    private void InitializeWorkspace()
    {
        _currentLayout = _persistenceService.LoadLayout();
        if (_currentLayout.Name == FactoryPresets.PresetPersonalizado)
            _savedCustomLayout = _currentLayout.Clone();
        else if (System.IO.File.Exists(_customLayoutService.LayoutFilePath))
        {
            var stored = _customLayoutService.LoadLayout();
            if (stored.Name == FactoryPresets.PresetPersonalizado) _savedCustomLayout = stored;
        }
        ApplyLayoutToUI(_currentLayout);
        SaveLayoutState();
    }

    private void ApplyLayoutToUI(WorkspaceLayout layout)
    {
        // 1. Árbol de acople
        DockContainer.SetLayout(layout.RootNode);

        // 2. Tarjetas flotantes internas MDI
        DockContainer.SyncFloatingCards(layout.FloatingPanels);

        // 3. Estante y menús
        ShelfNotch.UpdateMinimizedShelf(layout.MinimizedPanels);
        ShelfNotch.UpdatePanelsMenuState(layout);

        ActiveLayoutLabel.Text = layout.Name;
        StatusBarLayout.Text = $"Diseño: {layout.Name}";
        StatusBarStatus.Text = "Espacio de trabajo restaurado";
    }

    private void HandleFloatPanel(string panelId)
    {
        DockContainer.CaptureSplitRatios();
        _currentLayout.RootNode = LayoutNode.Remove(_currentLayout.RootNode, panelId);
        DockContainer.SetLayout(_currentLayout.RootNode);

        _currentLayout.MinimizedPanels.Remove(panelId);
        _currentLayout.ClosedPanels.Remove(panelId);
        _currentLayout.FloatingPanels.RemoveAll(f => string.Equals(f.PanelId, panelId, StringComparison.OrdinalIgnoreCase));

        var desc = new FloatingWindowDescriptor(panelId, 250, 80, 420, 320);
        _currentLayout.FloatingPanels.Add(desc);

        DockContainer.AddFloatingCard(desc);
        DockContainer.PositionFloatingCardUnderNotch(panelId);
        MarkAsCustomAndSave();
        StatusBarStatus.Text = $"Panel flotante: {PanelRegistry.GetPanelTitle(panelId)}";
    }

    private void HandlePanelDocked(string panelId, LayoutNode newRoot)
    {
        _currentLayout.RootNode = newRoot;
        _currentLayout.FloatingPanels.RemoveAll(f => string.Equals(f.PanelId, panelId, StringComparison.OrdinalIgnoreCase));
        _currentLayout.MinimizedPanels.Remove(panelId);
        _currentLayout.ClosedPanels.Remove(panelId);

        MarkAsCustomAndSave();
        StatusBarStatus.Text = $"Panel acoplado: {PanelRegistry.GetPanelTitle(panelId)}";
    }

    private void HandleDroppedAsFloating(string panelId, Point localPos)
    {
        DockContainer.CaptureSplitRatios();
        _currentLayout.RootNode = LayoutNode.Remove(_currentLayout.RootNode, panelId);
        DockContainer.SetLayout(_currentLayout.RootNode);

        _currentLayout.MinimizedPanels.Remove(panelId);
        _currentLayout.ClosedPanels.Remove(panelId);
        _currentLayout.FloatingPanels.RemoveAll(f => string.Equals(f.PanelId, panelId, StringComparison.OrdinalIgnoreCase));

        var desc = new FloatingWindowDescriptor(panelId, Math.Max(10, localPos.X - 50), Math.Max(10, localPos.Y - 20), 420, 320);
        _currentLayout.FloatingPanels.Add(desc);

        DockContainer.AddFloatingCard(desc);
        MarkAsCustomAndSave();
        StatusBarStatus.Text = $"Panel flotante liberado: {PanelRegistry.GetPanelTitle(panelId)}";
    }

    private void HandleMinimizePanel(string panelId)
    {
        DockContainer.RemoveFloatingCard(panelId);
        if (_currentLayout.RootNode?.FindNode(panelId) != null)
        {
            DockContainer.CaptureSplitRatios();
            _currentLayout.RootNode = LayoutNode.Remove(_currentLayout.RootNode, panelId);
            DockContainer.SetLayout(_currentLayout.RootNode);
        }

        _currentLayout.FloatingPanels.RemoveAll(f => string.Equals(f.PanelId, panelId, StringComparison.OrdinalIgnoreCase));
        _currentLayout.ClosedPanels.Remove(panelId);

        if (!_currentLayout.MinimizedPanels.Contains(panelId, StringComparer.OrdinalIgnoreCase))
        {
            _currentLayout.MinimizedPanels.Add(panelId);
        }

        ShelfNotch.UpdateMinimizedShelf(_currentLayout.MinimizedPanels);
        ShelfNotch.UpdatePanelsMenuState(_currentLayout);
        MarkAsCustomAndSave();
        StatusBarStatus.Text = $"Panel minimizado al Estante: {PanelRegistry.GetPanelTitle(panelId)}";
    }

    private void HandleClosePanel(string panelId)
    {
        DockContainer.RemoveFloatingCard(panelId);
        if (_currentLayout.RootNode?.FindNode(panelId) != null)
        {
            DockContainer.CaptureSplitRatios();
            _currentLayout.RootNode = LayoutNode.Remove(_currentLayout.RootNode, panelId);
            DockContainer.SetLayout(_currentLayout.RootNode);
        }

        _currentLayout.FloatingPanels.RemoveAll(f => string.Equals(f.PanelId, panelId, StringComparison.OrdinalIgnoreCase));
        _currentLayout.MinimizedPanels.Remove(panelId);

        if (!_currentLayout.ClosedPanels.Contains(panelId, StringComparer.OrdinalIgnoreCase))
        {
            _currentLayout.ClosedPanels.Add(panelId);
        }

        ShelfNotch.UpdateMinimizedShelf(_currentLayout.MinimizedPanels);
        ShelfNotch.UpdatePanelsMenuState(_currentLayout);
        MarkAsCustomAndSave();
        StatusBarStatus.Text = $"Panel cerrado: {PanelRegistry.GetPanelTitle(panelId)}";
    }

    private void HandleRestoreMinimizedAsFloating(string panelId)
    {
        _currentLayout.MinimizedPanels.Remove(panelId);
        _currentLayout.ClosedPanels.Remove(panelId);
        _currentLayout.FloatingPanels.RemoveAll(f => string.Equals(f.PanelId, panelId, StringComparison.OrdinalIgnoreCase));
        ShelfNotch.UpdateMinimizedShelf(_currentLayout.MinimizedPanels);

        var desc = new FloatingWindowDescriptor(panelId, 250, 80, 420, 320);
        _currentLayout.FloatingPanels.Add(desc);

        DockContainer.AddFloatingCard(desc);
        DockContainer.PositionFloatingCardUnderNotch(panelId);
        MarkAsCustomAndSave();
        StatusBarStatus.Text = $"Panel restaurado debajo del Estante: {PanelRegistry.GetPanelTitle(panelId)}";
    }

    private void HandlePanelDirectAction(string panelId, PanelDockState currentState)
    {
        switch (currentState)
        {
            case PanelDockState.Docked:
                // Si está acoplado: hacer brillar sus bordes para saber dónde está
                DockContainer.PulsePanelIfDocked(panelId);
                StatusBarStatus.Text = $"Panel localizado: {PanelRegistry.GetPanelTitle(panelId)}";
                break;

            case PanelDockState.Floating:
                // Si está flotante: traerlo justo debajo del notch y al frente
                DockContainer.PositionFloatingCardUnderNotch(panelId);
                StatusBarStatus.Text = $"Panel flotante enfocado: {PanelRegistry.GetPanelTitle(panelId)}";
                break;

            case PanelDockState.Minimized:
            case PanelDockState.Closed:
                // Si está cerrado o minimizado: abrirlo flotante justo debajo del notch
                HandleRestoreMinimizedAsFloating(panelId);
                break;
        }
    }

    private void ShowMcpReview(System.Text.Json.JsonElement args, object evaluation)
    {
        HandlePanelDirectAction("instructions", _currentLayout.GetPanelState("instructions"));
        if (PanelRegistry.GetExistingPanelContent("instructions") is InstructionsPanelView panel && _mcpHost != null)
        {
            panel.Configure(_mcpHost.EvaluateLocalAsync, _mcpHost.ApplyLocal);
            panel.ShowReview(args, evaluation);
        }
    }

    private void ApplyPreset(string presetName)
    {
        DockContainer.CaptureSplitRatios();
        if (_currentLayout.Name == FactoryPresets.PresetPersonalizado)
        {
            _currentLayout.FloatingPanels = DockContainer.GetFloatingDescriptors();
            _savedCustomLayout = _currentLayout.Clone();
            _customLayoutService.SaveLayout(_savedCustomLayout);
        }
        var preset = presetName == FactoryPresets.PresetPersonalizado
            ? _savedCustomLayout?.Clone() ?? _currentLayout.Clone()
            : FactoryPresets.GetPresetByName(presetName);
        if (presetName == FactoryPresets.PresetPersonalizado)
        {
            preset.Name = FactoryPresets.PresetPersonalizado;
            preset.IsFactoryPreset = false;
        }
        _currentLayout = preset;
        ApplyLayoutToUI(_currentLayout);
        _persistenceService.SaveLayout(_currentLayout);
    }

    private void ResetCurrentPreset()
    {
        if (_currentLayout.Name == FactoryPresets.PresetPersonalizado)
        {
            _savedCustomLayout = null;
            if (System.IO.File.Exists(_customLayoutService.LayoutFilePath))
                System.IO.File.Delete(_customLayoutService.LayoutFilePath);
            _currentLayout = FactoryPresets.CreateEquilibrado();
            ApplyLayoutToUI(_currentLayout);
            _persistenceService.SaveLayout(_currentLayout);
        }
        else ApplyPreset(_currentLayout.Name);
    }

    private void HandleLayoutModified()
    {
        MarkAsCustomAndSave();
    }

    private void MarkAsCustomAndSave()
    {
        _currentLayout.Name = FactoryPresets.PresetPersonalizado;
        _currentLayout.IsFactoryPreset = false;

        _currentLayout.FloatingPanels = DockContainer.GetFloatingDescriptors();

        ActiveLayoutLabel.Text = _currentLayout.Name;
        ShelfNotch.UpdatePanelsMenuState(_currentLayout);
        StatusBarLayout.Text = $"Diseño: {_currentLayout.Name}";

        SaveLayoutState();
    }

    private void SaveLayoutState()
    {
        DockContainer.CaptureSplitRatios();
        _currentLayout.FloatingPanels = DockContainer.GetFloatingDescriptors();
        _persistenceService.SaveLayout(_currentLayout);
        if (_currentLayout.Name == FactoryPresets.PresetPersonalizado)
        {
            _savedCustomLayout = _currentLayout.Clone();
            _customLayoutService.SaveLayout(_savedCustomLayout);
        }
        StatusBarStatus.Text = "Diseño guardado en disco";
    }

    private void HandleReplacePanel(string currentId, string replacementId)
    {
        if (currentId == replacementId || _currentLayout.RootNode?.FindNode(currentId) is not { } leaf) return;
        if (_currentLayout.GetPanelState(replacementId) is PanelDockState.Docked or PanelDockState.Floating)
        {
            StatusBarStatus.Text = "Ese panel ya está abierto; ciérralo o minimízalo antes de sustituir otro";
            return;
        }
        DockContainer.CaptureSplitRatios();
        leaf.PanelId = replacementId;
        _currentLayout.MinimizedPanels.Remove(replacementId);
        _currentLayout.ClosedPanels.Remove(replacementId);
        _currentLayout.MinimizedPanels.Remove(currentId);
        _currentLayout.MinimizedPanels.Add(currentId);
        DockContainer.SetLayout(_currentLayout.RootNode);
        ShelfNotch.UpdateMinimizedShelf(_currentLayout.MinimizedPanels);
        MarkAsCustomAndSave();
        StatusBarStatus.Text = $"Panel sustituido: {PanelRegistry.GetPanelTitle(replacementId)}";
    }
}
