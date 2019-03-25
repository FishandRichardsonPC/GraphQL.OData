using System.IO;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace GraphQL.OData.Example.MSGraph
{
	public class Program
	{
		public static void Main(string[] args)
		{
			Program.CreateWebHostBuilder(args).Build().Run();
		}

		private static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
			WebHost.CreateDefaultBuilder(args)
				.ConfigureAppConfiguration((hostingContext, config) =>
				{
					config.SetBasePath(Directory.GetCurrentDirectory());
					config.AddJsonFile("appsettings.json", true, true);
					config.AddJsonFile(
						$"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json",
						true
					);
					config.AddCommandLine(args);
					config.AddEnvironmentVariables();
				})
				.UseStartup<Startup>();
	}
}
