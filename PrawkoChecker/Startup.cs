using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using PrawkoChecker.Controllers;
using PrawkoChecker.Models;
using PrawkoChecker.Services;
using Serilog;

namespace PrawkoChecker
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.Configure<Settings>(this.Configuration.GetSection("Settings"));
            services.AddSingleton<CheckingController>();
            services.AddSingleton<NotificationService>();
            services.AddSingleton<DbService>();
            services.AddHangfire(x => x.UseMemoryStorage());
            services.AddHangfireServer();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, CheckingController checkingController, DbService dbService)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .CreateLogger();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            if (env.IsProduction())
            {
                Log.Information("Production");
                app.UseForwardedHeaders(new ForwardedHeadersOptions
                {
                    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                });
            }
            
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseHangfireDashboard();
            app.UseHangfireServer();

            checkingController.Configure().GetAwaiter().GetResult();

            const string jobId = nameof(CheckingController);
            RecurringJob.AddOrUpdate(jobId, () => checkingController.CheckAllSubscribedPkks(), Cron.Hourly);
            RecurringJob.Trigger(jobId);
            Log.Information(JsonConvert.SerializeObject(dbService.Get<ContactInfo>(), Formatting.Indented));
            Log.Information(JsonConvert.SerializeObject(dbService.Get<PkkData>(), Formatting.Indented));
        }
    }
}
