using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.Models;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.File;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        const string directoryName = "Directory";
        const string connectionString = "DefaultEndpointsProtocol=https;AccountName=ashleys;AccountKey=1r59/RlH1IhcgnMwn+Dv9VaRVFyg2yQcKiI0o8SFYtMWvJyhP4Pv57FV4idB5PbiPOTSaXttJqYSeX5Ybar3xA==;EndpointSuffix=core.windows.net";
        const string fileShareName = "new-file-share";

        static CloudStorageAccount storageAccount = CreateStorageAccountFromConnectionString(connectionString);
        static CloudFileClient fileClient = storageAccount.CreateCloudFileClient();
        CloudFileShare share = fileClient.GetShareReference(fileShareName);


        private static CloudStorageAccount CreateStorageAccountFromConnectionString(string storageConnectionString)
        {
            CloudStorageAccount storageAccount;
            try
            {
                storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            }
            catch (FormatException)
            {
                Console.WriteLine("Invalid storage account information provided. Please confirm the AccountName and AccountKey are valid in the app.config file - then restart the sample.");
                Console.WriteLine("Press any key to exit");
                Console.ReadLine();
                throw;
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Invalid storage account information provided. Please confirm the AccountName and AccountKey are valid in the app.config file - then restart the sample.");
                Console.WriteLine("Press any key to exit");
                Console.ReadLine();
                throw;
            }

            return storageAccount;
        }

        public IActionResult Index()
        {
            string policy = cleanupAndGetPolicyToUse().Result;
            
            List<IListFileItem> files = getFilesFromAzure().Result;
            
            CloudFileDirectory dir = share.GetRootDirectoryReference().GetDirectoryReference(directoryName);
            List<ReturnedFile> fileData = new List<ReturnedFile>();
            foreach(var f in files)
            {
                string fileName = (f as CloudFile).Name;
                CloudFile file = dir.GetFileReference(fileName);
                string signature = file.GetSharedAccessSignature(null, policy);
                Uri fileSasUri = new Uri(file.StorageUri.PrimaryUri.ToString() + signature);
                fileData.Add(new ReturnedFile(fileName, fileSasUri));
            }
            
            ViewBag.files = fileData;

            return View();
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost("UploadFiles")]
        public async Task<IActionResult> Post(List<IFormFile> files)
        {

            CloudFileDirectory root = share.GetRootDirectoryReference();
            CloudFileDirectory dir = root.GetDirectoryReference(directoryName);
            await dir.CreateIfNotExistsAsync();

            long size = files.Sum(f => f.Length);

            foreach (var formFile in files)
            {
                if (formFile.Length > 0)
                {
                    CloudFile file = dir.GetFileReference(formFile.FileName);
                    await file.UploadFromStreamAsync(formFile.OpenReadStream());
                }
            }

            // process uploaded files
            // Don't rely on or trust the FileName property without validation.

            return RedirectToAction("Index");
        }

        public async Task<List<IListFileItem>> getFilesFromAzure()
        {
            FileSharePermissions permissions = await share.GetPermissionsAsync();
            // List all files/directories under the root directory
            Console.WriteLine("4. List Files/Directories in root directory");
            List<IListFileItem> results = new List<IListFileItem>();
            FileContinuationToken token = null;
            
            do
            {
                FileResultSegment resultSegment = await share.GetRootDirectoryReference().GetDirectoryReference(directoryName).ListFilesAndDirectoriesSegmentedAsync(token);
                results.AddRange(resultSegment.Results);
                token = resultSegment.ContinuationToken;
            }
            while (token != null);
            return results;
        }

        public async Task<string> cleanupAndGetPolicyToUse()
        {
            FileSharePermissions permissions = await share.GetPermissionsAsync();
            List<string> policiesToDelete = permissions.SharedAccessPolicies.TakeWhile(x => x.Value.SharedAccessExpiryTime < DateTime.UtcNow).Select(x => x.Key).ToList();
            foreach(var policy in policiesToDelete)
            {
                permissions.SharedAccessPolicies.Remove(policy);
            }

            string policyNameToUse = permissions.SharedAccessPolicies.Where(x => x.Value.SharedAccessExpiryTime > DateTime.UtcNow.AddSeconds(10)).Select(x => x.Key).FirstOrDefault();

            if(policyNameToUse == null)
            {
                // Create a new shared access policy and define its constraints.
                SharedAccessFilePolicy sharedPolicy = new SharedAccessFilePolicy()
                {
                    SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(1),
                    Permissions = SharedAccessFilePermissions.Read
                };
                policyNameToUse = "UniquePolicy" + DateTime.UtcNow.Ticks;
                permissions.SharedAccessPolicies.Add(policyNameToUse, sharedPolicy);
            }

            await share.SetPermissionsAsync(permissions);

            return policyNameToUse;
        }

        public async Task<ActionResult> DeleteFile(string fileName)
        {
            CloudFileDirectory dir = share.GetRootDirectoryReference().GetDirectoryReference(directoryName);
            CloudFile file = dir.GetFileReference(fileName);
            await file.DeleteIfExistsAsync();
            return RedirectToAction("Index");
        }
    }

    public class ReturnedFile
    {
        public string Name { get; }
        public Uri Uri { get; set; }

        public ReturnedFile(string name, Uri uri)
        {
            this.Name = name;
            this.Uri = uri;
        }
    }
}
