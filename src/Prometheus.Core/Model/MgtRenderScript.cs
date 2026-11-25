using Acornima;
using Acornima.Ast;

namespace Prometheus.Core.Model;

/// <summary>Accept both MGT render bodies and complete function values without evaluating source.</summary>
public static class MgtRenderScript
{
    public static string FunctionBody(string source)
    {
        var expression = source.Trim().TrimEnd(';');
        try
        {
            var script = new Parser().ParseScript("(" + expression + "\n)");
            if (script.Body.Count == 1 && script.Body[0] is ExpressionStatement statement &&
                statement.Expression is FunctionExpression or ArrowFunctionExpression)
                return "return (" + expression + "\n)(ctx);";
        }
        catch (ParseErrorException) { }
        return source;
    }
}
