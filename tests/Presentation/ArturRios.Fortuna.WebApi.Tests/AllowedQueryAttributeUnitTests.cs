using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Filters;
using ArturRios.Output;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class AllowedQueryAttributeUnitTests
{
    [UnitFact]
    public void GivenOnlyAllowedParametersInAnyCase_WhenExecuting_ThenRequestContinues()
    {
        var context = Context("?pagenumber=1&PAGESIZE=10", nameof(Sample.Page));

        new AllowedQueryAttribute("PageNumber", "PageSize").OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    [UnitFact]
    public void GivenUnknownParameterOnPagedAction_WhenExecuting_ThenPagedBadRequestNamesIt()
    {
        var context = Context("?PageNumber=1&Institution=Bank", nameof(Sample.Page));

        new AllowedQueryAttribute("PageNumber").OnActionExecuting(context);

        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        var output = Assert.IsType<PaginatedOutput<object>>(result.Value);
        Assert.Equal([QueryParameterMessages.Unsupported("Institution")], output.Errors);
    }

    [UnitFact]
    public void GivenUnknownParameterOnSingleAction_WhenExecuting_ThenDataBadRequestNamesIt()
    {
        var context = Context("?Text=a&Unknown=b", nameof(Sample.Single));

        new AllowedQueryAttribute("Text").OnActionExecuting(context);

        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        var output = Assert.IsType<DataOutput<object?>>(result.Value);
        Assert.Equal([QueryParameterMessages.Unsupported("Unknown")], output.Errors);
    }

    private static ActionExecutingContext Context(string queryString, string method)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString(queryString);
        var action = new ControllerActionDescriptor
        {
            MethodInfo = typeof(Sample).GetMethod(method)!
        };

        return new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), action),
            [],
            new Dictionary<string, object?>(),
            new object());
    }

    private sealed class Sample
    {
        public Task<ActionResult<PaginatedOutput<string>>> Page() =>
            throw new NotSupportedException();

        public Task<ActionResult<DataOutput<string?>>> Single() =>
            throw new NotSupportedException();
    }
}
