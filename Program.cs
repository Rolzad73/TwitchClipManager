﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace ClipManager
{
    class ClipInfo
    {
        public string id { get; set; }
        public int view_count { get; set; }
        public string creator_name { get; set; }
        public string created_at { get; set; }
        public string title { get; set; }
    }
    class Program
    {
        static string TwitchClientID = "kimne78kx3ncx6brgo4mv6wki5h1ko";
        static string TwitchToken;
        static string UserId;
        static string Login;
        static string Cursor;
        static string StartDateTime;
        static string EndDateTime;
        static bool Download = false;
        static bool Delete = false;
        static string RootPath = Environment.CurrentDirectory;
        static void Main(string[] args)
        {
            Console.WriteLine($"--== TwitchClipDownloader ==--");
            LoadConfig();
            GetUserID();
            var folder = Path.Combine(RootPath, "downloads");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            var clips = GetClips();
            while (clips.Count > 0)
            {
                foreach (var clip in clips)
                {
                    if (DateTime.Parse(clip.created_at) >= 
                        DateTime.Parse(StartDateTime) &&  
                        DateTime.Parse(clip.created_at) <= 
                        DateTime.Parse(EndDateTime))
                    {
                        var fileName = SanitizeFile($"{clip.created_at} - {clip.title} - {clip.creator_name} - {clip.id}.mp4");
                        var savePath = Path.Combine(folder, fileName);
                        try
                        {
                            if (Download)
                            {
                                Console.WriteLine($"Downloading {clip.id} - {clip.title} by {clip.creator_name}");
                                var sourceUrl = GetClipUri(clip.id);
                                DownloadClip(sourceUrl, savePath);
                            }
                            else
                            {
                                Console.WriteLine($"Found {fileName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(Path.Combine(RootPath, "error.log"), $"{clip.id} download failed: {ex.Message}");
                        }
                    }
                }
                // TODO: this delete removes batch of returns, need to adjust to individually delete on date range filter match
                if (Delete)
                {
                    Console.WriteLine($"Deleting {string.Join(',', clips.Select(c => c.id))}");
                    DeleteClips(clips.Select(c => c.id).ToList());
                }
                UpdateCursor();
                if (!Delete && Cursor == null)
                    break;
                clips = GetClips();
            }
            Console.WriteLine($"--== FIN ==--");
        }

        static void LoadConfig()
        {
            var configPath = Path.Combine(RootPath, "appsettings.json");
            bool resume = false;
            if (File.Exists(configPath))
            {
                Console.WriteLine("Session found resume? (y or n):");
                var resumeResp = Console.ReadLine();
                resume = resumeResp.ToLower().StartsWith('y');
                if (resume)
                {
                    var fs = File.OpenRead(configPath);
                    var fsr = new StreamReader(fs);
                    var config = JObject.Parse(fsr.ReadToEnd());
                    Cursor = config["cursor"]?.ToString();
                    TwitchToken = config["twitchtoken"]?.ToString();

                    StartDateTime = config["startdatetime"]?.ToString();
                    EndDateTime = config["enddatetime"]?.ToString();

                    Download = config["download"]?.ToObject<bool>() == true;
                    Delete = config["delete"]?.ToObject<bool>() == true;
                }
            }
            if (!resume)
            {
                if (File.Exists(configPath))
                    File.Delete(configPath);
                GetConfig();
            }
        }

        static void GetConfig()
        {
            Console.WriteLine("Paste in auth token:");
            TwitchToken = Console.ReadLine().Trim();

            Console.WriteLine("Start Date ('yyyy-MM-ddThh:mm:ss' or blank for very first):");
            var startDateTimeResp = Console.ReadLine();
            if (startDateTimeResp == "") StartDateTime = DateTime.UnixEpoch.ToString();
            else StartDateTime = startDateTimeResp;

            Console.WriteLine("End Date ('yyyy-MM-ddThh:mm:ss' or blank for very last):");
            var endDateTimeResp = Console.ReadLine();
            if (endDateTimeResp == "") EndDateTime = DateTime.UtcNow.ToString();
            else EndDateTime = endDateTimeResp;


            Console.WriteLine("Download (y or n):");
            var downloadResp = Console.ReadLine();
            Download = downloadResp.ToLower().StartsWith('y');

            Console.WriteLine("Delete (y or n):");
            var deleteResp = Console.ReadLine();
            Delete = deleteResp.ToLower().StartsWith('y');
            if (Delete)
            {
                Console.WriteLine("Are you REALLY sure you want to Delete (non-reversible!) (y or n):");
                var deleteRespConfirm = Console.ReadLine();
                Delete = deleteRespConfirm.ToLower().StartsWith('y');
            }

            var configPath = Path.Combine(RootPath, "appsettings.json");
            var config = new JObject()
            {
                ["twitchtoken"] = TwitchToken,
                ["startdatetime"] = StartDateTime,
                ["enddatetime"] = EndDateTime,

                ["download"] = Download,
                ["delete"] = Delete
            };
            var fsw = File.OpenWrite(configPath);
            var sw = new StreamWriter(fsw);
            sw.Write(config.ToString());
            sw.Close();
        }

        static void UpdateCursor()
        {
            var configPath = Path.Combine(RootPath, "appsettings.json");
            JObject config = new JObject();
            if (File.Exists(configPath))
            {
                var fsr = File.OpenRead(configPath);
                var sr = new StreamReader(fsr);
                config = JObject.Parse(sr.ReadToEnd());
                fsr.Close();

                File.Delete(configPath);
            }
            config["cursor"] = Cursor;

            var fsw = File.OpenWrite(configPath);
            var sw = new StreamWriter(fsw);
            sw.Write(config.ToString());
            sw.Close();
        }

        static void GetUserID()
        {
            try
            {
                var http = new HttpClient();
                http.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TwitchToken);

                var res = http.GetStringAsync($"https://api.twitch.tv/helix/users").GetAwaiter().GetResult();
                var jtok = JToken.Parse(res);
                UserId = jtok["data"][0]["id"].ToString();
                Login = jtok["data"][0]["login"].ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{TwitchClientID} get user id failed: {ex.Message}");
                System.Environment.Exit(1);
            }
        }

        static IList<ClipInfo> GetClips()
        {
            var gql = new JArray()
            {
                new JObject()
                {
                    ["extensions"] = new JObject()
                    {
                        ["persistedQuery"] = new JObject()
                        {
                            ["version"] = 1,
                            ["sha256Hash"] = "b300f79444fdcf2a1a76c101f466c8c9d7bee49b643a4d7878310a4e03944232"
                        }
                    },
                    ["operationName"] = "ClipsManagerTable_User",
                    ["variables"] = new JObject()
                    {
                        ["login"] = Login,
                        ["limit"] = 5,
                        ["criteria"] = new JObject()
                        {
                            ["sort"] = "VIEWS_DESC",
                            ["period"] = "ALL_TIME",
                            ["broadcasterID"] = UserId
                        }
                    }
                }
            };
            if (!Delete && !string.IsNullOrWhiteSpace(Cursor))
            {
                gql[0]["variables"]["cursor"] = Cursor;
            }
            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var ghttp = new HttpClient();
            ghttp.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            ghttp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", TwitchToken);
            // TODO: need to change this to official "helix" api as using this gql way may have consequences
            var res = ghttp.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var jtok = JArray.Parse(res);
            var retVal = new List<ClipInfo>();
            bool hasNextPage = jtok[0]["data"]["user"]["clips"]["pageInfo"]["hasNextPage"]?.ToObject<bool>() == true;
            foreach (dynamic e in jtok[0]["data"]["user"]["clips"]["edges"])
            {
                dynamic node = e.node;
                string creator = node.broadcaster.login;
                try
                {
                    creator = node.curator.login;
                }
                catch
                {

                }
                retVal.Add(new ClipInfo
                {
                    id = node.slug,
                    title = node.title,
                    creator_name = creator,
                    created_at = DateTime.Parse(Convert.ToString(node.createdAt)).ToString("yyyy-MM-ddThh:mm:ss"),
                    view_count = node.viewCount
                });
                if (!Delete && e.cursor != null && hasNextPage)
                {
                    Cursor = e.cursor;
                }
            }
            if (!Delete && !hasNextPage) Cursor = null;
            return retVal;
        }

        static string GetClipUri(string clipId)
        {
            var gql = new JArray();
            gql.Add(new JObject()
            {
                ["extensions"] = new JObject()
                {
                    ["persistedQuery"] = new JObject()
                    {
                        ["version"] = 1,
                        ["sha256Hash"] = "9bfcc0177bffc730bd5a5a89005869d2773480cf1738c592143b5173634b7d15"
                    }
                },
                ["operationName"] = "VideoAccessToken_Clip",
                ["variables"] = new JObject()
                {
                    ["slug"] = clipId
                }
            });
            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var ghttp = new HttpClient();
            ghttp.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            var res = ghttp.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var jtok = JArray.Parse(res);
            return jtok[0]["data"]["clip"]["videoQualities"][0]["sourceURL"].ToString();
        }

        static void DeleteClips(IList<string> clips)
        {
            var gql = new JArray();
            gql.Add(new JObject()
            {
                ["extensions"] = new JObject()
                {
                    ["persistedQuery"] = new JObject()
                    {
                        ["version"] = 1,
                        ["sha256Hash"] = "df142a7eec57c5260d274b92abddb0bd1229dc538341434c90367cf1f22d71c4"
                    }
                },
                ["operationName"] = "Clips_DeleteClips",
                ["variables"] = new JObject()
                {
                    ["input"] = new JObject()
                    {
                        ["slugs"] = new JArray(clips.ToArray())
                    }
                }
            });
            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var ghttp = new HttpClient();
            ghttp.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            ghttp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", TwitchToken);
            while (true)
            {
                var res = ghttp.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var jtok = JArray.Parse(res);
                if (!res.Contains("error"))
                    break;
                else
                    Thread.Sleep(10000);
            }
        }

        static void DownloadClip(string sourceUrl, string savePath)
        {
            var http = new HttpClient();
            var stream = http.GetStreamAsync(sourceUrl).GetAwaiter().GetResult();
            if (File.Exists(savePath))
                File.Delete(savePath);
            var fs = new FileStream(savePath, FileMode.CreateNew);
            stream.CopyTo(fs);
            fs.Close();
        }

        static string SanitizeFile(string origFileName)
        {
            var invalids = Path.GetInvalidFileNameChars();
            return string.Join("_", origFileName.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }
    }
}
