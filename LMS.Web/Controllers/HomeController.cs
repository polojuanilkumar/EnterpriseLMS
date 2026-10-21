using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using LMS.Web.Models;

namespace LMS.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return RedirectToAction("Organizations", "Account");
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
