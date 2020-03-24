using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using GraphQL.Server;
using GraphQL.Server.Authorization.AspNetCore;
using GraphQL.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;

namespace GraphQL.OData.Example.MSGraph
{
	public static class AadExtensionMethods
	{
		public static IGraphQLBuilder AddAadPolicy(this IGraphQLBuilder builder)
		{
			return builder.AddGraphQLAuthorization(
				options =>
				{
					options.AddPolicy(
						"AAD",
						policy => policy.RequireAuthenticatedUser().Build());
				});
		}
	}

	public class AadManager
	{
		private readonly IHttpContextAccessor _contextAccessor;

		public AadManager(IHttpContextAccessor contextAccessor)
		{
			this._contextAccessor = contextAccessor;
		}

		public HttpRequestMessage PreRequest(
			IResolveFieldContext context,
			HttpRequestMessage message
		)
		{
			return this.PreRequestAsync(context, message).Result;
		}

		public async Task<HttpRequestMessage> PreRequestAsync(
			IResolveFieldContext context,
			HttpRequestMessage message
		)
		{
			var token = await this._contextAccessor.HttpContext.GetTokenAsync("access_token");
			if (token == null)
			{
				throw new AuthenticationException("Failed to acquire access token");
			}

			message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			return message;
		}

		public void AddAuthentication(GraphType type)
		{
			type.AuthorizeWith("AAD");
		}
	}

	public class AuthenticationException : Exception {
		public AuthenticationException(string message): base(message)
		{
		}
	}
}
