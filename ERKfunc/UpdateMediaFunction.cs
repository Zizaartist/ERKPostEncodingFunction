// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace ERKfunc
{
    public class UpdateMediaFunction
    {
        private readonly ConfigWrapper _config;
        private readonly IConfiguration _configForDB;

        public UpdateMediaFunction(IOptions<ConfigWrapper> config, IConfiguration configForDB)
        {
            _config = config.Value;
            _configForDB = configForDB;
        }

        [FunctionName("UpdateMediaFunction")]
        public async Task Run([EventGridTrigger] EventGridEvent eventGridEvent, ILogger log)
        {
            JObject dataJson = JObject.FromObject(eventGridEvent.Data);
            var outputsJson = dataJson["outputs"];
            var correlationDataJson = dataJson["correlationData"];
            var template = new { postMediaId = 0, inputAssetName = "" };

            var output = JsonConvert.DeserializeObject<IEnumerable<JobOutputAsset>>(outputsJson.ToString()).First();
            var correlationData = JsonConvert.DeserializeAnonymousType(correlationDataJson.ToString(), template);

            string resourceGroup = _config.ResourceGroup;
            string accountName = _config.AccountName;
            string transformName = _config.VideoEncoderName;
            var inputAssetName = correlationData.inputAssetName;
            var outputAssetName = output.AssetName;
            var jobState = output.State;
            var jobName = eventGridEvent.Subject.Split('/').Last();
            int postMediaId = correlationData.postMediaId;

            var servicesClient = await AzureHelper.CreateMediaServicesClientAsync(_config);

            //Если задание успешно завершено - создать streamLocator и перезаписать запись медиа файла в базе данных
            if (jobState == JobState.Finished)
            {
                var streamingLocator = await AzureHelper.CreateStreamingLocatorAsync(servicesClient, resourceGroup, accountName, outputAssetName, $"{outputAssetName}Locator");
                var urls = await AzureHelper.GetStreamingUrlAsync(servicesClient, resourceGroup, accountName, streamingLocator.Name);

                await UpdateDBRecord(urls[0], urls[1], postMediaId);
            }
            else if (jobState == JobState.Error || jobState == JobState.Canceled)
            {
                await AzureHelper.CleanUpOutputAsset(servicesClient, resourceGroup, accountName, outputAssetName);
                await RemoveDBRecord(postMediaId);
            }
            else 
            {
                throw new ApiErrorException(message: "Триггер сработал неверно, необработанный State задания");
            }

            //Очистка ненужных ресурсов
            await AzureHelper.CleanUpAsync(servicesClient, resourceGroup, accountName, transformName, jobName, inputAssetName);
        }

        private async Task UpdateDBRecord(string videoPath, string previewPath, int mediaId)
        {
            var connStr = _configForDB["ConnectionStrings:DefaultConnection"];
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                var query = $"UPDATE [dbo].[PostMedia] SET [Path] = \'{videoPath}\' ,[PreviewPath] = \'{previewPath}\' ,[MediaType] = {1} WHERE [PostMediaId] = {mediaId} ";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    // Execute the command and log the # rows affected.
                    var rows = await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task RemoveDBRecord(int mediaId)
        {
            var connStr = _configForDB["ConnectionStrings:DefaultConnection"];
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                var query = $"DELETE FROM [dbo].[PostMedia] WHERE [PostMediaId] = {mediaId} ";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    // Execute the command and log the # rows affected.
                    var rows = await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}