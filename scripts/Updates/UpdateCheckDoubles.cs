using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Wavebreak.Updates;

internal sealed class UpdateCheckHandler : HttpMessageHandler
{
    internal Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new(HttpStatusCode.NotFound);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    { cancel.ThrowIfCancellationRequested(); return Task.FromResult(Respond(request)); }
}
internal sealed class UpdateCheckHandoff : IUpdateHandoff
{
    internal int Calls;
    public Task<bool> StartAsync(UpdateContext context, UpdateRelease release, PreparedUpdate prepared,
        bool restart, string language, CancellationToken cancel) { Calls++; return Task.FromResult(false); }
}
