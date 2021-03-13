using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MimeKit;
using Serilog;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using PrawkoChecker.Models;
using Newtonsoft.Json;

namespace PrawkoChecker.Services
{
    public class Message
    {
        [JsonProperty("registration_ids")]
        public string[] Token { get; set; }

        [JsonProperty("notification")]
        public Notification Notification { get; set; }
    }

    public class Notification
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }

    public class NotificationService
    {
        private readonly DbService dbService;
        private static Dictionary<string, ContactInfo> contacts = new();
        private Settings settings;

        public NotificationService(DbService dbService, IOptions<Settings> options)
        {
            this.dbService = dbService;
            this.settings = options.Value;
        }

        public void Configure()
        {
            contacts = this.dbService.Get<ContactInfo>()
                .ToDictionary(x => x.Pkk, x => x);
        }

        public async Task Notify(PkkStatusResponse statusResponse, PkkData data)
        {
            var contact = contacts[data.Pkk];
            if (!string.IsNullOrWhiteSpace(contact.Email))
            {
                await this.SendEmail(contact.Email, statusResponse);
                Log.Information("Email sent");
            }

            if (!string.IsNullOrWhiteSpace(contact.AndroidAppClientId))
            {
                await this.SendStatusNotification(contact.AndroidAppClientId, statusResponse);
                Log.Information("Notification sent");
            }
        }

        public async Task AddOrUpdateContact(string pkk, string email = null, string androidAppClientId = null)
        {
            Log.Information("Adding new contact {pkk}", pkk);
            var contactInfo = new ContactInfo
            {
                Pkk = pkk,
                Email = email,
                AndroidAppClientId = androidAppClientId
            };
            if (contacts.ContainsKey(pkk))
            {
                contacts[pkk] = contactInfo;
            }
            else
            {
                contacts.Add(pkk, contactInfo);
            }
            this.dbService.AddOrUpdate(contactInfo);

            if (!string.IsNullOrWhiteSpace(androidAppClientId))
            {
                _ = this.SendWelcomeNotification(androidAppClientId);
            }
            if (!string.IsNullOrWhiteSpace(email))
            {
                await this.SendWelcomeEmail(email);
            }
            Log.Information("Contact added {pkk}", pkk);
        }

        public void RemoveContact(string pkk)
        {
            var toDeleteEntity = this.dbService.Get<ContactInfo>().FirstOrDefault(x => x.Pkk == pkk);
            if (toDeleteEntity != null)
            {
                this.dbService.Delete(toDeleteEntity);
            }
            contacts.Remove(pkk);
            Log.Information("Contact removed {pkk}", pkk);
        }

        private async Task SendEmail(string email, PkkStatusResponse statusResponse)
        {
            var message = new MimeMessage
            {
                Body = new TextPart {Text = statusResponse.StatusHistory.Last().Description},
                From = {InternetAddress.Parse(this.settings.EmailAddress)},
                To = {InternetAddress.Parse(email)},
                Subject = "Status prawa jazdy zmieniony - Prawko Checker"
            };
            await this.SendEmail(message);
            Log.Information("Status email sent");
        }

        private async Task SendStatusNotification(string androidAppClientId, PkkStatusResponse statusResponse)
        {
            var notification = new Notification()
            {
                Title = "Zmiana statusu",
                Text = statusResponse.StatusHistory.Last().Description
            };
            await this.SendNotification(notification, androidAppClientId);
            Log.Information("Status notification sent");
        }

        private async Task SendWelcomeNotification(string clientAppId)
        {
            await Task.Delay(30 * 1000);
            var notification = new Notification
            {
                Title = "Powitalne powiadomienie",
                Text = "Takie powiadomienie dostaniesz kiedy zmieni się status twojego prawa jazdy"
            };
            await this.SendNotification(notification, clientAppId);
            Log.Information("Welcome notification sent");
        }

        private async Task SendWelcomeEmail(string email)
        {
            var message = new MimeMessage
            {
                Body = new TextPart
                {
                    Text = "Cześć! Ten email został dodany do bazy. Dostaniesz na niego powiadomienia o zmianie statusu prawa jazdy"
                },
                From = {InternetAddress.Parse(this.settings.EmailAddress)},
                To = {InternetAddress.Parse(email)},
                Subject = "Adres dodany do PrawkoChecker"
            };
            await this.SendEmail(message);
            Log.Information("Welcome email sent");
        }

        private async Task SendNotification(Notification notification, string clientAppId)
        {
            var messageInformation = new Message
            {
                Notification = notification,
                Token = new []{ clientAppId }
            };

            var jsonMessage = JsonConvert.SerializeObject(messageInformation);

            const string firebaseUrl = "https://fcm.googleapis.com/fcm/send";
            var request = new HttpRequestMessage(HttpMethod.Post, firebaseUrl);
            request.Headers.TryAddWithoutValidation("Authorization", "key =" + this.settings.FirebaseServerKey);
            request.Content = new StringContent(jsonMessage, Encoding.UTF8, "application/json");
            using var client = new HttpClient();
            var result = await client.SendAsync(request);
            if (result.IsSuccessStatusCode)
            {
                Log.Information("Notification success");
            }
            else
            {
                Log.Error("Notification error");
            }
        }

        private async Task SendEmail(MimeMessage message)
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(this.settings.HostSmtp, this.settings.PortSmtp, true);
            await client.AuthenticateAsync(this.settings.EmailAddress, this.settings.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}