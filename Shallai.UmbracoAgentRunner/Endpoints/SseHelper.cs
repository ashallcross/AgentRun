using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Shallai.UmbracoAgentRunner.Endpoints;

public static class SseHelper
{
    public static void ConfigureSseResponse(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        var bufferingFeature = response.HttpContext.Features
            .Get<IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();
    }
}
