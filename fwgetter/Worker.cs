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

        private readonly string FwgetterDir = Environment.GetEnvironmentVariable("FWGETTER_PATH", EnvironmentVariableTarget.User) ?? "C:\\";//sets the dir to wright files to, either by an enviormental var or {FwgetterDir}. 
        
        private delegate void QuitDel();
        private event QuitDel OnQuit; 
        private List<FileStream> streams = new List<FileStream>();

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

       
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogCritical($"using path: {FwgetterDir}");
            if (File.Exists($@"{FwgetterDir}\ipsw\last.bin"))//load past history
            {
                var serializer = new BinaryFormatter();
                await using var fileStream = File.Open($@"{FwgetterDir}\ipsw\last.bin", FileMode.Open);
                lastUpdates = (Dictionary<string, string>)serializer.Deserialize(fileStream);
                 _logger.LogCritical("Loaded update history at " + DateTimeOffset.Now);

            }
            else
            {
                lastUpdates = new Dictionary<string, string>();
                _logger.LogWarning("didn't load update history");

            }

            if (!Directory.Exists($@"{FwgetterDir}\ipsw\"))
            {
                Directory.CreateDirectory($@"{FwgetterDir}\ipsw\");
            }


            await base.StartAsync(cancellationToken);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug("entered");
            var client = new RestClient("https://api.ipsw.me/v4/");
            var requestDevices = new RestRequest("devices");
            var requestFirmware = new RestRequest("device/{id}?type=ipsw");
            var po = new ParallelOptions { CancellationToken = stoppingToken };

            while (!stoppingToken.IsCancellationRequested)//endless cycle
            {
                var devices = client.Execute<List<JsonReps.device>>(requestDevices).Data;
                devices.ForEach((item) => _logger.LogDebug(item.identifier));
                var firmwareListings = new List<JsonReps.FirmwareListing>();


                Parallel.ForEach(devices, po, (device) => //get firmwares for all devices
                {
                    var tempReq = new RestRequest("device/{id}?type=ipsw");
                    _logger.LogDebug("getting devices...");
                    var resp = client.Execute<JsonReps.FirmwareListing>(tempReq.AddUrlSegment("id",
                        device.identifier.Replace(" ", string.Empty)));
                    _logger.LogDebug(resp.Data.name);
                    firmwareListings.Add(resp.Data);
                    _logger.LogDebug(firmwareListings.Count.ToString());
                    
                });

                foreach (var device in firmwareListings)//create dir for new devices found
                {
                    device.name = device.name.Replace($@" / ", "l");//clean names
                    
                    if (!Directory.Exists($@"{FwgetterDir}\ipsw\{device.name}"))
                    {
                        _logger.LogDebug($@"creating new directory {FwgetterDir}\ipsw\{device.name}");
                        Directory.CreateDirectory($@"{FwgetterDir}\ipsw\{device.name}");
                    }
                    
                }

                
                Parallel.ForEach(firmwareListings, po, async firmwareListing => 
                {
                    var requestDownload = new RestRequest();
                    var clientApple = new RestClient();

                    try
                    {
                        if (!lastUpdates.Contains(new KeyValuePair<string, string>(firmwareListing.name,
                                firmwareListing.firmwares[0].buildid)) && !File.Exists(
                                $@"{FwgetterDir}\ipsw\{firmwareListing.name}\{firmwareListing.name},{firmwareListing.firmwares[0].version},{firmwareListing.firmwares[0].buildid}.ipsw")
                        )

                        {
                            await using var writer =
                                File.Create(
                                    $@"{FwgetterDir}\ipsw\{firmwareListing.name}\{firmwareListing.name},{firmwareListing.firmwares[0].version},{firmwareListing.firmwares[0].buildid}.ipsw");

                            streams.Add(writer);

                            requestDownload.ResponseWriter =
                                stream => //sets the request to write straight to disk, skipping memory buffers
                                {
                                    using (stream)
                                    {
                                        stream.CopyTo(writer);
                                    }

                                };

                            requestDownload.Resource = firmwareListing.firmwares[0].url;

                            _logger.LogInformation(
                                $"starting download of {firmwareListing.name},{firmwareListing.firmwares[0].buildid}");

                            QuitDel delFile = () => { File.Delete($@"{FwgetterDir}\ipsw\{firmwareListing.name}\{firmwareListing.name},{firmwareListing.firmwares[0].version},{firmwareListing.firmwares[0].buildid}.ipsw");};

                            OnQuit += delFile;//add file to be deleted if DL doesn't finish

                            var response = await Task.Run(() => client.DownloadData(requestDownload), stoppingToken);

                            OnQuit -= delFile;//file will no longer be deleted

                            _logger.LogInformation($"finished download of {firmwareListing.name},{firmwareListing.firmwares[0].buildid}");

                        }
                    }

                    catch (OperationCanceledException)
                    {
                        _logger.LogCritical("operation cancelled");

                    }

                    catch (Exception)
                    {

                        _logger.LogWarning($"no IPSW found for {firmwareListing.name}");

                    }

                });

                foreach (var firmwareListing in firmwareListings)//update update history
                {
                    try
                    {
                        if (!lastUpdates.Contains(new KeyValuePair<string, string>(firmwareListing.name, firmwareListing.firmwares[0].buildid)))
                        {
                            lastUpdates.Add(firmwareListing.name, firmwareListing.firmwares[0].buildid);
                            _logger.LogInformation(
                                $"added entry to update history: {firmwareListing.name},{firmwareListing.firmwares[0].version},{firmwareListing.firmwares[0].buildid}");
                        }
                    }
                    catch (Exception)
                    {
                        //ignore, this just means the device has no firmware
                    }
                }

                await Task.Delay(1800000, stoppingToken);
            }

            await StopAsync(stoppingToken);
        }


        public override Task StopAsync(CancellationToken cancellationToken)
        {
            streams.ForEach(stream => {stream.Dispose();});//close all FileStreams to allow for deletion

            OnQuit?.Invoke();//delete unfinished files

            var serializer = new BinaryFormatter();

            if (!File.Exists($@"{FwgetterDir}\ipsw\last.bin"))
            {
                File.Create($@"{FwgetterDir}\ipsw\last.bin").Dispose();
                
            }

            serializer.Serialize(File.Open($@"{FwgetterDir}\ipsw\last.bin",FileMode.Create),lastUpdates);

            if (OnQuit?.GetInvocationList().Length > 0)//if there was any files deleted, the last downloaded is invalid. TODO optimize this.
            {
                File.Delete($@"{FwgetterDir}\ipsw\last.bin");
            }

            _logger.LogCritical("successfully saved update log ");
            return base.StopAsync(cancellationToken);
        }
    }
}
