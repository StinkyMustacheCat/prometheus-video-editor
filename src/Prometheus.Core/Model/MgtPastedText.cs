using System.Text.Json;

namespace Prometheus.Core.Model;

/// <summary>Creates a portable MGT composition when plain text is pasted into the media pool.</summary>
public static class MgtPastedText
{
    public static string Create(string text, string aspect = "horizontal")
    {
        text = text.Trim();
        if (text.Length is < 1 or > 10_000)
            throw new InvalidDataException("El texto MGT debe tener entre 1 y 10 000 caracteres.");
        if (aspect is not ("horizontal" or "vertical" or "square" or "classic"))
            throw new ArgumentOutOfRangeException(nameof(aspect));
        var title = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "Texto MGT";
        if (title.Length > 60) title = title[..57] + "…";
        var document = new
        {
            version = 4,
            kind = "composition",
            name = title,
            seed = 1,
            recommended = new { duration = 5, aspect },
            parameters = new object[]
            {
                new { id = "title", label = "Texto", type = "text", @default = text },
                new { id = "color", label = "Color", type = "color", @default = "#ffffff" },
                new { id = "font", label = "Fuente", type = "font", @default = "Arial" },
                new { id = "size", label = "Tamaño", type = "number", @default = 130, min = 24, max = 400, step = 1 }
            },
            html = "<div id='mgt-pasted-title'></div>",
            css = "#mgt-pasted-title{position:absolute;inset:8%;display:flex;align-items:center;justify-content:center;text-align:center;white-space:pre-wrap;overflow-wrap:anywhere;line-height:1.12;font-weight:700;text-shadow:0 2px 8px #0009}",
            render = "const node=ctx.root.querySelector('#mgt-pasted-title');node.textContent=ctx.params.title;node.style.color=ctx.params.color;node.style.fontFamily=ctx.params.font;node.style.fontSize=ctx.params.size+'px';"
        };
        return JsonSerializer.Serialize(document);
    }
}
