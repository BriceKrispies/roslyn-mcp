using MediatR;
using MyApp.ViewModels;

namespace MyApp.Messages;

public record GetUsersQuery : IRequest<UsersViewModel>;
