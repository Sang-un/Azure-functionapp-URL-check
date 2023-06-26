using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
// new using
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using System.Diagnostics;
using Microsoft.ApplicationInsights.DataContracts;
using System.Collections;



// 망분리 환경에서 코드 업로드
namespace Company.Function
{
    public class TimerTrigger1
    {
        private static readonly TelemetryClient _telemetryClient = new TelemetryClient(TelemetryConfiguration.CreateDefault());

        private readonly ILogger _logger;
        private ArrayList results = new ArrayList();

        public TimerTrigger1(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<TimerTrigger1>();
        }

        [Function("TimerTrigger1")]
        public async Task RunAsync([TimerTrigger("0 */5 * * * *")] MyInfo myTimer)
        {
            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");


            Guid operationId = Guid.NewGuid();
            Guid requestId = Guid.NewGuid();


            List<Task<AvailabilityTelemetry>> results = new List<Task<AvailabilityTelemetry>>();
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key.ToString().Contains("APPSETTING_URL"))
                {
                    _logger.LogInformation($"{entry.Key}");
                    _logger.LogInformation($"{entry.Value}");

                    results.Add(TestAvailabilityTelemetry(operationId, requestId, new Uri(entry.Value.ToString()), (String)entry.Key));

                }

            }

            await Task.WhenAll(results);

            foreach (var result in results)
            {
                
                _logger.LogInformation("=====================================");
                _logger.LogInformation($"{result.Result.Name}");
                _logger.LogInformation($"{result.Result.Message}");
                _logger.LogInformation($"{result.Result.Success}");
            }


        }
        public async Task<HttpResponseMessage> ExecuteHttpRequestAsync(Uri requestUri)
        {
            HttpClientHandler clientHandler = new HttpClientHandler();
            clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };

            using (HttpClient client = new HttpClient(clientHandler))
            {


                var responseTask = client.GetAsync(requestUri);
                var completedTask = await Task.WhenAny(responseTask, Task.Delay(TimeSpan.FromSeconds(30)));

                if (completedTask != responseTask)
                {
                    _logger.LogError("Timeout Exception");

                    throw new TimeoutException("Timeout Exception");
                }

                HttpResponseMessage response = await responseTask;


                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Latest Run status is not success.");
                    // throw new Exception("Latest Run status is not success.");
                }

                return response;
            }
        }
        public async Task<AvailabilityTelemetry> TestAvailabilityTelemetry(Guid operationId, Guid requestId, Uri requestUri, String key)
        {

            int duration = 0;
            AvailabilityTelemetry availability = new AvailabilityTelemetry();
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                HttpResponseMessage response = await ExecuteHttpRequestAsync(requestUri);
                stopwatch.Stop();


                availability.Message = (response.StatusCode <= System.Net.HttpStatusCode.PartialContent) ?
                    ((int)response.StatusCode).ToString() :
                    ((int)response.StatusCode).ToString() + " " + response.ReasonPhrase;

                availability.Success = response.IsSuccessStatusCode;
                availability.Properties["FullTestResultAvailable"] = "true";

            }
            //  Timeout Exception
            catch (Exception ex)
            {
                _logger.LogError($"Error occurred during HTTP request: {ex.Message}");
                availability.Message = ex.Message;
                availability.Success = false;
                availability.Properties["FullTestResultAvailable"] = "true";
                availability.Properties["Test success"] = "false";
            }
            finally
            {
                duration = (int)stopwatch.ElapsedMilliseconds;
                DateTime dt = DateTime.Now;

                availability.Duration = TimeSpan.FromMilliseconds(duration);

                availability.Id = requestId.ToString();
                availability.Name = "" + requestUri;

                availability.RunLocation = "Korea Central";

                availability.Timestamp = dt;

                _telemetryClient.Context.Operation.Id = operationId.ToString();
                _telemetryClient.TrackAvailability(availability);
                _telemetryClient.Flush();

            }
            return availability;


        }
    }

    public class MyInfo
    {
        public MyScheduleStatus ScheduleStatus { get; set; }

        public bool IsPastDue { get; set; }
    }

    public class MyScheduleStatus
    {
        public DateTime Last { get; set; }

        public DateTime Next { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
