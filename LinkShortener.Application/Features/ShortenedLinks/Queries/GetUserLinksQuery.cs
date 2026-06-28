using MediatR;
using System;
using System.Collections.Generic;

namespace LinkShortener.Application.Features.ShortenedLinks.Queries.GetUserLinks;

public sealed record GetUserLinksQuery(Guid UserId) : IRequest<List<ShortenedLinkDto>>;