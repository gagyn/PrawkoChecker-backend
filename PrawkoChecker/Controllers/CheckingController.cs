using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using PrawkoChecker.Models;
using PrawkoChecker.Services;
using Serilog;

namespace PrawkoChecker.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CheckingController : ControllerBase
    {
        private readonly NotificationService notificationService;
        private readonly DbService dbService;

        private const string API_ADDRESS = "https://info-car.pl/api/ssi/status/driver/driver-licence";

        private static HttpClient httpClient;
        private static List<PkkData> subscribedPkks = new();
        private static readonly Dictionary<string, int> lastStatusCounts = new(); // pkk | statusHistoryCount

        public CheckingController(NotificationService notificationService, DbService dbService)
        {
            this.notificationService = notificationService;
            this.dbService = dbService;
            httpClient = new HttpClient
            {
                BaseAddress = new Uri(API_ADDRESS)
            };
        }

        [HttpGet("subscribe")]
        public async Task<IActionResult> SubscribePkk(string name, string surname, string pkk, string email = null, string androidClientId = null)
        {
            Log.Information($"{name} {surname} {pkk} {email} {androidClientId}");

            if (subscribedPkks.Any(x => x.Pkk == pkk))
            {
                Log.Warning("Już subsrybujesz dany numer PKK ({pkk})", pkk);
                return this.BadRequest("Już subsrybujesz dany numer PKK");
            }
            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(androidClientId))
            {
                Log.Warning("Nie podano adresu email ({pkk})", pkk);
                return this.BadRequest("Nie podano adresu email");
            }
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(surname) || string.IsNullOrWhiteSpace(pkk))
            {
                Log.Warning("Nie podano wystarczającej liczby danych ({pkk})", pkk);
                return this.BadRequest("Nie podano wystarczającej liczby danych");
            }

            var pkkData = new PkkData
            {
                Pkk = pkk,
                Name = name,
                Surname = surname
            };

            var currentStatus = await this.CheckStatus(pkkData);
            currentStatus ??= await this.CheckStatus(pkkData);
            if (currentStatus == null)
            {
                return this.BadRequest("Nie można pobrać statusu dla tego numeru PKK");
            }

            var statusCount = new StatusCount
            {
                Pkk = pkk,
                Count = currentStatus.StatusHistory.Count
            };

            subscribedPkks.Add(pkkData);
            lastStatusCounts.Add(pkk, currentStatus.StatusHistory.Count);
            this.dbService.AddOrUpdate(pkkData);
            this.dbService.AddOrUpdate(statusCount);
            await this.notificationService.AddOrUpdateContact(pkk, email, androidClientId);
            return this.Ok("Dodano numer PKK");
        }

        [HttpGet("unsubscribe")]
        public IActionResult UnsubscribePkk(string pkk)
        {
            var toDelete = subscribedPkks.FirstOrDefault(x => x.Pkk == pkk);
            if (toDelete == null)
            {
                return this.NotFound("Obecnie nie subskrybujesz powiadomień dla tego PKK");
            }
            subscribedPkks.Remove(toDelete);
            lastStatusCounts.Remove(pkk);
            var toDeleteEntity = this.dbService.Get<PkkData>().FirstOrDefault(x => x.Pkk == pkk);
            if (toDeleteEntity != null)
            {
                this.dbService.Delete(toDeleteEntity);
            }
            this.notificationService.RemoveContact(pkk);
            return this.Ok("Odsubskrybowano");
        }

        [HttpGet("current")]
        public async Task<IActionResult> GetCurrentStatus(string pkk)
        {
            var pkkData = subscribedPkks.FirstOrDefault(x => x.Pkk == pkk);
            if (pkkData == null)
            {
                return this.BadRequest("Najpierw musisz dodać ten numer PKK");
            }
            var status = await this.CheckStatus(pkkData);
            if (status == null)
            {
                return this.BadRequest("Nie można pobrać statusu dla tego numeru PKK");
            }
            return this.Ok(status.StatusHistory.Last());
        }

        public async Task Configure()
        {
            subscribedPkks = this.dbService.Get<PkkData>().ToList();
            Log.Information("Got {count} pkks from db", subscribedPkks.Count);
            foreach (var subscribedPkk in subscribedPkks)
            {
                var response = await this.CheckStatus(subscribedPkk);
                if (response == null)
                {
                    continue;
                }
                lastStatusCounts.Add(subscribedPkk.Pkk, response.StatusHistory.Count);
            }
            Log.Information("Configured checker");
            this.notificationService.Configure();
            Log.Information("Notifications configured");
        }

        public async Task CheckAllSubscribedPkks()
        {
            Log.Information("Checking pkks");
            foreach (var subscribedPkk in subscribedPkks)
            {
                var response = await this.CheckStatus(subscribedPkk);
                response ??= await this.CheckStatus(subscribedPkk);
                if (response == null)
                {
                    continue;
                }
                Log.Information("Pkk {pkk} has {count} statusCount", subscribedPkk.Pkk, response.StatusHistory.Count);
                if (lastStatusCounts[subscribedPkk.Pkk] == response.StatusHistory.Count)
                {
                    continue;
                }

                Log.Information("Status count changed from {olderValue} to {newValue}", 
                    lastStatusCounts[subscribedPkk.Pkk], response.StatusHistory.Count);

                lastStatusCounts[subscribedPkk.Pkk] = response.StatusHistory.Count;
                var status = new StatusCount
                {
                    Pkk = subscribedPkk.Pkk,
                    Count = response.StatusHistory.Count
                };
                this.dbService.AddOrUpdate(status);
                await this.notificationService.Notify(response, subscribedPkk);
            }
            Log.Information("Pkks checked");
        }

        private async Task<PkkStatusResponse> CheckStatus(PkkData pkkData)
        {
            Log.Information("Sending request for {pkkData}", pkkData.Pkk);
            try
            {
                var response = await httpClient.PostAsJsonAsync("", pkkData);
                Log.Information("Got response");
                response.EnsureSuccessStatusCode();
                var statusResponse = await response.Content.ReadFromJsonAsync<PkkStatusResponse>();
                return statusResponse;
            }
            catch (Exception e)
            {
                Log.Error("Nie można pobrać statusu dla numeru {pkk}", pkkData.Pkk);
                Log.Error(e.Message);
                Log.Error(e.StackTrace);
                return null;
            }
        }
    }
}
