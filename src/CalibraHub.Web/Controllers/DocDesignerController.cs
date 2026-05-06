using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class DocDesignerController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult New() => View("Edit", 0);

    [HttpGet("{id:int}")]
    public IActionResult Edit(int id) => View(id);
}
