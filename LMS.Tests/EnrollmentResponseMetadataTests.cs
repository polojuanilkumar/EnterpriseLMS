using System.Reflection;
using LMS.Api.Controllers;
using LMS.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LMS.Tests;

public sealed class EnrollmentResponseMetadataTests
{
    // Expected sets are based on the controller, authorization attributes, and handler paths.
    [Theory]
    [InlineData(nameof(EnrollmentsController.Enroll), new[] { 201, 401, 403, 404, 409 })]
    [InlineData(nameof(EnrollmentsController.Cancel), new[] { 200, 401, 403, 404, 409 })]
    [InlineData(nameof(EnrollmentsController.Complete), new[] { 200, 401, 403, 404, 409 })]
    [InlineData(nameof(EnrollmentsController.GetById), new[] { 200, 401, 403, 404 })]
    [InlineData(nameof(EnrollmentsController.GetMyEnrollments), new[] { 200, 401 })]
    [InlineData(nameof(EnrollmentsController.GetMyCoursesProgress), new[] { 200, 401, 403 })]
    public void Declared_responses_match_reachable_action_outcomes(string action, int[] expected)
    {
        var attributes = typeof(EnrollmentsController).GetMethod(action)!
            .GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        Assert.Equal(expected, attributes.Select(a => a.StatusCode).OrderBy(s => s).ToArray());
        foreach (var attribute in attributes.Where(a => a.StatusCode is 403 or 404 or 409))
            Assert.Equal(typeof(ApiResponse<object>), attribute.Type);
    }
}
