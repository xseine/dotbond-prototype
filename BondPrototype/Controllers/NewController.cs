using Microsoft.AspNetCore.Mvc;

namespace TestingBond.Controllers;
// asaaa
[ApiController]
[Route("[controller]/[action]")]
public class NewController : ControllerBase
{
    // GET
    public string Test()
    {
        return "savo";
    }

    public string TestTwo()
    {
        return "savo 2";
    }

    public string TestThree()
    {
        return "savo 3";
    }


    public string TestFour()
    {
        return "savo 4";
    }

    // aaaaaaaaa
    public async Task<ActionResult<Jack>> TestNine()
    {
        await Task.Delay(1);
        return Ok();
    }
    

    public class Jack
    {
        
    }

}
