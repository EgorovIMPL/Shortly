using Grpc.Core;
using MediatR;
using Shortly.Application.Links;

namespace Shortly.Api.Grpc;

public sealed class LinkGrpcService : LinkService.LinkServiceBase
{
    private readonly ISender _sender;

    public LinkGrpcService(ISender sender) => _sender = sender;

    public override async Task<ResolveReply> Resolve(ResolveRequest request, ServerCallContext context)
    {
        var result = await _sender.Send(new ResolveLinkQuery(request.Code), context.CancellationToken);

        return result is null
            ? new ResolveReply { Found = false }
            : new ResolveReply
            {
                Found = true,
                OriginalUrl = result.OriginalUrl,
                LinkId = result.LinkId
            };
    }

    public override async Task<CreateReply> Create(CreateRequest request, ServerCallContext context)
    {
        var command = new CreateLinkCommand(
            request.OriginalUrl,
            string.IsNullOrWhiteSpace(request.CustomCode) ? null : request.CustomCode,
            null);

        var dto = await _sender.Send(command, context.CancellationToken);

        return new CreateReply { Code = dto.Code, ShortUrl = dto.ShortUrl };
    }
}