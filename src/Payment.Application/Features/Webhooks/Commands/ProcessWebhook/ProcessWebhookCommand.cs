using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Webhooks.Commands.ProcessWebhook;

public record ProcessWebhookCommand(
    string Payload,
    string Signature
) : IRequest<Result>;
