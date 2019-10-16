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

        private Dictionary<string, string> lastUpdates = new Dictionary<string, string>();//phone name, buildId 

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

       
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            if (File.Exists(@"C:\ipsw\last.bin"))
            {
                var serializer = new BinaryFormatter();
                await using var fileStream = File.Open(@"C:\ipsw\last.bin", FileMode.Open);
                lastUpdates = (Dictionary<string, string>)serializer.Deserialize(fileStream);
                 _logger.LogCritical("Loaded update history at " + DateTimeOffset.Now);

            }
            else
            {
                lastUpdates = new Dictionary<string, string>();
                _logger.LogWarning("didn't load update history");

            }

            if (!Directory.Exists("C:\\ipsw\\"))
            {
                Directory.CreateDirectory("C:\\ipsw\\");
            }


            await base.StartAsync(cancellationToken);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug("entered");
            var client = new RestClient("https://api.ipsw.me/v4/");
            var requestDevices = new RestRequest("devices");
            var requestFirmware = new RestRequest("device/{id}?type=ipsw");
            var requestDownload = new RestRequest("/ipsw/download/{identifier}/{buildid}");

            while (!stoppingToken.IsCancellationRequested)//endless cycle
            {
                var devices = client.Execute<List<JsonReps.device>>(requestDevices);
                devices.Data.ForEach((item) => _logger.LogDebug(item.identifier));
                var firmwareListings = new List<JsonReps.FirmwareListing>();

                foreach (var device in devices.Data)//get firmwares for all devices
                {
                    var tempReq = new RestRequest("device/{id}?type=ipsw");
                    _logger.LogDebug("getting devices...");
                    var resp = client.Execute<JsonReps.FirmwareListing>(tempReq.AddUrlSegment("id", device.identifier.Replace(" ",string.Empty)));
                    _logger.LogDebug(resp.Data.name);
                    firmwareListings.Add(resp.Data);
                    _logger.LogDebug(firmwareListings.Count.ToString());
                }

                foreach (var device in firmwareListings)//create dir for new devices found
                {
                    
                    if (!Directory.Exists($@"C:\ipsw\{device.name}"))
                    {
                        _logger.LogDebug($@"creating new directory C:\ipsw\{device.name}");
                        Directory.CreateDirectory($@"C:\ipsw\{device.name}");
                    }
                    
                }
                
                foreach (var firmwareListing in firmwareListings)
                {   
                    requestDownload = new RestRequest("/ipsw/download/{identifier}/{buildid}");

                    _logger.LogDebug(firmwareListing.firmwares[0].buildid);
                    if (!lastUpdates.Contains(new KeyValuePair<string, string>(firmwareListing.name, firmwareListing.firmwares[0].buildid)))
                    {
                        await using var writer = File.Create($@"C:\ipsw\{firmwareListing.name}\{firmwareListing.name},{firmwareListing.firmwares[0].version},{firmwareListing.firmwares[0].buildid}.ipsw");

                        requestDownload.ResponseWriter = stream =>//sets the request to write straight to disk, skipping memory buffers
                        {
                            using (stream)
                            {
                                stream.CopyTo(writer);
                            }

                        };

                        var response = client.DownloadData(requestDownload.AddUrlSegment("identifier",firmwareListing.identifier).AddUrlSegment("buildid",firmwareListing.firmwares[0].buildid));

                        _logger.LogInformation($"finished download of {firmwareListing.name},{firmwareListing.firmwares[0].buildid}");

                    }

                }

                foreach (var firmwareListing in firmwareListings)//update update history
                {
                    if (!lastUpdates.Contains(new KeyValuePair<string, string>(firmwareListing.name,
                        firmwareListing.firmwares[0].buildid)))
                    {
                        lastUpdates.Add(firmwareListing.name,firmwareListing.firmwares[0].buildid);
                        _logger.LogInformation($"added entry to update history: {firmwareListing.name},{firmwareListing.firmwares[0].version},{firmwareListing.firmwares[0].buildid}");
                    }

                }


                await Task.Delay(10000, stoppingToken);
            }
        }


        public override Task StopAsync(CancellationToken cancellationToken)
        {
            var serializer = new BinaryFormatter();

            if (!File.Exists(@"C:\ipsw\last.bin"))
            {
                File.Create(@"C:\ipsw\last.bin").Dispose();
                
            }

            serializer.Serialize(File.Open(@"C:\ipsw\last.bin",FileMode.Create),lastUpdates);

            _logger.LogCritical("successfully saved update log ");
            return base.StopAsync(cancellationToken);
        }
    }
}
