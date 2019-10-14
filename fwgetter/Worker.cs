using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using RestSharp;

namespace fwgetter
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private Dictionary<string, string> lastUpdates;//phone name, buildId 
        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

       
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            if (File.Exists(@".\last.bin"))
            {
                var serializer = new BinaryFormatter();
                await using var fileStream = File.Open(@".\last.bin", FileMode.Open);
                lastUpdates = (Dictionary<string, string>)serializer.Deserialize(fileStream);
                 _logger.LogCritical("Loaded update history at " + DateTimeOffset.Now);

            }
            else
            {
                lastUpdates = new Dictionary<string, string>();
                _logger.LogWarning("didn't load update history");

            }

            await base.StartAsync(cancellationToken);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var client = new RestClient("https://api.ipsw.me/v4/");
            var requestDevices = new RestRequest("devices");
            var requestFirmware = new RestRequest("device/{id}{?type}");


            while (!stoppingToken.IsCancellationRequested)
            {
                  





                await Task.Delay(10000, stoppingToken);
            }
        }


        public override Task StopAsync(CancellationToken cancellationToken)
        {
            var serializer = new BinaryFormatter();

            if (!File.Exists(@".\last.bin"))
            {
                File.Create(@".\last.bin").Dispose();
                
            }

            serializer.Serialize(File.Open(@".\last.bin",FileMode.Create),lastUpdates);

            _logger.LogCritical("successfully saved update log ");
            return base.StopAsync(cancellationToken);
        }
    }
}
