namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>
///     The OpenAPI document's <c>info.description</c>: what its responses cannot say, so the document a client
///     vendors states it.
/// </summary>
public static class ApiDocumentDescription
{
    public const string Text =
        """
        Fortuna's personal finance API. Every response a client can receive is listed on its operation, from every
        layer: the handler, the filters, model binding, the authentication and rate-limiting middleware, the
        exception handler, Kestrel and, where the API is deployed behind it, Traefik. A few things a response
        cannot say are stated here.

        **Handler statuses are an upper bound.** A status the handler answers is listed when a message the code
        it reaches names maps to it in the operation's status map. Code shared by several handlers, such as a
        store or a helper that maps a whole feature's outcomes, brings every message it names, so an operation
        can list a status its handler never meets. It never leaves one out.

        **No response at all.** Some failures end the connection without a status:
        - a request body cut short of its `Content-Length`;
        - a request body sent too slowly, when the client is still sending as Kestrel closes the connection
          after its 408;
        - a client that disconnects;
        - a failure after the response has started, which aborts it;
        - a keep-alive connection closed when idle (Kestrel after 130 s, Traefik after 180 s);
        - a request Traefik stops reading after 60 s;
        - over HTTP/2 between the client and Traefik, a stream reset.

        **In front of the API.** In environments behind Cloudflare, Cloudflare answers some requests itself,
        with its own HTML pages and not this API's bodies: a request body over its limit (100 MB on the Free
        plan) with 413, and the origin failing with 520 to 526. Outside those, the deployment's own proxy answers
        only the 502 and 504 listed on every operation.

        **Development.** In the Development environment, an unexpected failure is the developer exception page
        (`text/html`), not the JSON 500 documented here.

        **Outside the operations.** A path that matches no operation, including an id that fails its route
        constraint (`{id:guid}`), gets 404, and a known path asked for with another method gets 405, both with no
        body.
        """;
}
