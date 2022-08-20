using Microsoft.AspNetCore.Mvc;

namespace BondPrototype.Controllers;
// aaa
[ApiController]
[Route("[controller]")]
public class TranslateDemoController : ControllerBase
{
    [HttpGet]
    public IActionResult Index(string source)
    {
        var translation = Translator.TranslateApi.TranslateDemo(source);
        return new JsonResult(translation);
    }


    /// <summary>
    /// Example of a HttpPost
    /// </summary>
    [HttpPost]
    public IActionResult Index([FromBody] string source, int? _)
    {
        return Index(source);
    }

    public record struct IndexParameter(string Source);
}