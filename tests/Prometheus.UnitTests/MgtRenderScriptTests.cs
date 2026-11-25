using System.Text.Json;
using Jint;
using Prometheus.Core.Model;

namespace Prometheus.UnitTests;

public class MgtRenderScriptTests
{
    [Theory]
    [InlineData("ctx.count++;")]
    [InlineData("(function render(ctx){ ctx.count++; })")]
    [InlineData("function render(ctx){ ctx.count++; };")]
    [InlineData("(ctx) => { ctx.count++; }")]
    [InlineData("/* exported render */ (function(ctx){ ctx.count++; });")]
    [InlineData("(function(){ ctx.count++; })();")]
    [InlineData("function helper(){ ctx.count++; } helper();")]
    public void SupportedRenderFormsExecuteExactlyOncePerFrame(string source)
    {
        var body = JsonSerializer.Serialize(MgtRenderScript.FunctionBody(source));
        var count = new Engine().Evaluate("const ctx={count:0}; const draw=new Function('ctx'," + body +
            "); draw(ctx); ctx.count;").AsNumber();
        Assert.Equal(1, count);
    }

    [Fact]
    public void NormalizingAFunctionDoesNotExecuteItsBody()
    {
        var source = "(function(ctx){ throw Error('Only at render time'); })";
        Assert.StartsWith("return (", MgtRenderScript.FunctionBody(source));
    }
}
