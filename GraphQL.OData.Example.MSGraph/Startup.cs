using GraphQL.Server;
using GraphQL.Server.Ui.GraphiQL;
using GraphQL.Server.Ui.Playground;
using GraphQL.Server.Ui.Voyager;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.AzureAD.UI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GraphQL.OData.Example.MSGraph
{
	public class Startup
	{
		private readonly IConfiguration _configuration;
		private readonly IWebHostEnvironment _environment;

		public Startup(IConfiguration configuration, IWebHostEnvironment environment)
		{
			this._configuration = configuration;
			this._environment = environment;
		}

		// This method gets called by the runtime. Use this method to add services to the container.
		// For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddGraphQLOData();
			services
				.AddGraphQL((options) =>
                {
                    options.EnableMetrics = true;
                    options.ExposeExceptions = this._environment.IsDevelopment();
				})
				.AddAadPolicy();

			services.AddAuthentication(AzureADDefaults.AuthenticationScheme)
				.AddAzureAD(
					options => { this._configuration.Bind("AzureAd", options); })
				.Services
				.Configure<CookieAuthenticationOptions>(
					AzureADDefaults.CookieScheme,
					o =>
					{
						o.Cookie.HttpOnly = false;
						o.Cookie.SameSite = SameSiteMode.None;
						o.Cookie.SecurePolicy = CookieSecurePolicy.None;
					}
				);


			services.Configure<OpenIdConnectOptions>(
				AzureADDefaults.OpenIdScheme,
				options =>
				{
					options.Authority = options.Authority + "/v2.0/"; // Azure AD v2.0

					options.TokenValidationParameters.ValidateIssuer = false; // accept several tenants (here simplified)
					options.SaveTokens = true;
					options.ResponseType = "token id_token";
				});

			services.AddSingleton<AadManager>();
			services.AddSingleton<Mutation>();
			services.AddSingleton<Query>();
			services.AddSingleton<Schema>();
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			if (env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}

			app.UseAuthentication();
			app.UseCookiePolicy();

			app.UseGraphQLPlayground(new GraphQLPlaygroundOptions());
			app.UseGraphQLVoyager(new GraphQLVoyagerOptions());
			app.UseGraphiQLServer(new GraphiQLOptions());
			app.UseGraphQL<Schema>();

			app.Run(
				async (context) =>
				{
					if (context.Request.Path == "/favicon.ico")
					{
						context.Response.StatusCode = 404;
						await context.Response.WriteAsync("");
						return;
					}

					if (!context.User.Identity.IsAuthenticated)
					{
						if (context.Request.Path == "/signin-oidc")
						{

						}
						await context.ChallengeAsync();
					}

					await context.Response.WriteAsync(
						"<a href='/ui/playground'>Playground</a><br>"
						+ "<a href='/ui/voyager'>Voyager</a><br>"
						+ "<a href='/graphiql'>GraphiQL</a>"
					);
				});
		}
	}
}
