using Microsoft.AspNetCore.Mvc;

namespace TestingBond.Controllers;
// asaaa
[ApiController]
[Route("[controller]/[action]")]
public class NewController : ControllerBase
{
    // GET
    [HttpGet]
    public string Test()
    {
        return "savo";
    }

    [HttpGet]
    public string TestTwo()
    {
        return "savo 2";
    }

    [HttpGet]
    public string TestThree()
    {
        return "savo 3";
    }

    [HttpGet]
    public string TestFour()
    {
        return "savo 4";
    }

    // aaaaaaaaa
    [HttpGet]
    public async Task<ActionResult<Jack>> TestNine()
    {
        await Task.Delay(1);
        return Ok();
    }
    

    public class Jack
    {
        
    }

}
