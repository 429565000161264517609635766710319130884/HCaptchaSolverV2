using HCaptcha_Solver.API;
using HCaptcha_SolverV2.Classes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace HCaptcha_SolverV2.API
{
    public class HCaptcha
    {
        private string Site { get; set; }

        private Random Random { get; set; }

        private Yolo Yolo { get; set; }

        private bool Headless { get; set; }

        private Browser Browser { get; set; }

        public HCaptcha(string site, bool yolo, bool headless = false)
        {
            Site = site;
            Headless = headless;
            if (yolo)
            {
                Yolo = new Yolo();
                Yolo.Setup();
            }
            Random = new Random();
        }

        private string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[Random.Next(s.Length)]).ToArray());
        }

        private string DownloadImage(string url)
        {
            var name = RandomString(20) + ".png";
            new WebClient().DownloadFile(url, $"Images\\{name}");
            return $"Images\\{name}";
        }

        public async Task<bool> SolveCaptcha()
        {
            Console.WriteLine("[!] Navigating [!]");
            await new BrowserFetcher().DownloadAsync();
            var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = Headless,
                Args = new string[]
                {
                    "--start-maximized"
                }
            });
            Browser = browser;
            var page = await browser.NewPageAsync();
            page.RequestFinished += Page_RequestFinished;
            await page.SetViewportAsync(new ViewPortOptions
            {
                Width = 1920 /2,
                Height = 1080
            });
            await page.GoToAsync(Site);
            await page.WaitForSelectorAsync("iframe[src^='https://newassets.hcaptcha.com/captcha/v1/']");
            await page.WaitForTimeoutAsync(5000);
            Console.WriteLine("[!] Collecting frames [!]");
            var checkbox = page.Frames.Where(x => x.Url.Contains("hcaptcha-checkbox.html")).First();
            var challenge = page.Frames.Where(x => x.Url.Contains("hcaptcha-challenge.html")).First();
            Console.WriteLine("[!] Checking box [!]");
            await checkbox.ClickAsync("#checkbox");
            Console.WriteLine("[!] Waiting for challenge frame to show [!]");
            await page.WaitForTimeoutAsync(5000);
            var text = await challenge.EvaluateExpressionAsync("document.querySelector('body > div.challenge-container > div > div > div.challenge-header > div.challenge-prompt > div.prompt-padding > div.prompt-text').innerHTML");
            var thing = text.ToString().Split(' ').Last().Replace("motorbus", "bus").Replace("airplane", "aeroplane");
            var crumbs = await challenge.EvaluateExpressionAsync("document.querySelector('body > div.challenge-interface > div.challenge-breadcrumbs > div').children.length");

            if (Convert.ToInt32(crumbs) == 0)
                return await SolveCaptcha(); //sometimes this happens :/

            Console.WriteLine($"[!] We are looking for: {thing} [!]");
            Console.WriteLine($"[!] Crumbs: {crumbs} [!]");
            for(var x = 0; x < Convert.ToInt32(crumbs.ToString()); x++)
            {
                var imageurls = new List<string>();

                for (var i = 1; i < 10; i++)
                {
                    var image = await challenge.EvaluateExpressionAsync($"document.querySelector('body > div.challenge-container > div > div > div.task-grid > div:nth-child({i}) > div.image-wrapper > div').style.background.replace('url(\"', '').replace('\")', '').split(' ')[0].replace(' ', '')");
                    imageurls.Add(image.ToString());
                }

                var j = 0;
                foreach (var imageurl in imageurls)
                {
                    j++;
                    if (Yolo == null)
                    {
                        var response = await new HttpClient().GetAsync($"https://www.imageidentify.com/objects/user-26a7681f-4b48-4f71-8f9f-93030898d70d/prd/urlapi?image={HttpUtility.UrlEncode(imageurl)}");
                        var json = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                        var identify = json.GetValue("identify").ToObject<JObject>();
                        var alternatives = identify.GetValue("alternatives").ToObject<JObject>();
                        var title = identify.GetValue("title").ToString();

                        if (title.ToLower().Contains(thing))
                        {
                            Console.WriteLine("[!] CLICKED FROM TITLE [!]");
                            await challenge.ClickAsync($"body > div.challenge-container > div > div > div.task-grid > div:nth-child({j}) > div.image-wrapper > div");
                            continue;
                        }

                        foreach (var what in alternatives.Children())
                        {
                            var property = what.ToObject<JProperty>();
                            var yep = false;

                            if (property.Name.ToLower().Contains(thing) && property.Value.ToString().ToLower().Contains(thing))
                            {
                                Console.WriteLine("[!] CLICKED FROM PROPERTY NAME AND VALUE [!]");
                                await challenge.ClickAsync($"body > div.challenge-container > div > div > div.task-grid > div:nth-child({j}) > div.image-wrapper > div");
                                yep = true;
                                continue;
                            }

                            if (property.Name.ToLower().Contains(thing) && !yep)
                            {
                                Console.WriteLine("[!] CLICKED FROM PROPERTY NAME [!]");
                                await challenge.ClickAsync($"body > div.challenge-container > div > div > div.task-grid > div:nth-child({j}) > div.image-wrapper > div");
                                yep = true;
                                continue;
                            }

                            if (property.Value.ToString().ToLower().Contains(thing) && !yep)
                            {
                                Console.WriteLine("[!] CLICKED FROM PROPERTY VALUE [!]");
                                await challenge.ClickAsync($"body > div.challenge-container > div > div > div.task-grid > div:nth-child({j}) > div.image-wrapper > div");
                                yep = true;
                                continue;
                            }
                        }
                    }
                    else
                    {
                        var clicked = false;
                        var path = DownloadImage(imageurl);
                        var objects = Yolo.DetectObjects(path);
                        foreach (var _object in objects)
                        {
                            Console.WriteLine($"{_object.Confidence} -> {_object.Type}");
                            if (_object.Type == thing && !clicked)
                            {
                                Console.WriteLine("[!] CLICKED FROM YOLO [!]");
                                clicked = true;
                                await challenge.ClickAsync($"body > div.challenge-container > div > div > div.task-grid > div:nth-child({j}) > div.image-wrapper > div");
                            }
                        }
                        File.Delete(path);
                    }
                }
                Console.WriteLine("[!] DONE RECOGNISING [!]");
                await challenge.WaitForTimeoutAsync(2000);
                await challenge.ClickAsync("body > div.challenge-interface > div.button-submit");
                Console.WriteLine("[!] NEXT/SUBMIT [!]");
            }

            await page.WaitForTimeoutAsync(5000);
            await browser.CloseAsync();
            return true;
        }

        private async void Page_RequestFinished(object sender, RequestEventArgs e)
        {
            Console.WriteLine(e.Request.Url + " -> " + e.Request.Method);
            if (e.Request.Url.Contains("checkcaptcha") && e.Request.Method == HttpMethod.Post)
            {
                var json = await e.Request.Response.JsonAsync();
                JToken uuid;
                if (json.TryGetValue("generated_pass_UUID", out uuid))
                {
                    Console.WriteLine("[!] TOKEN: " + uuid.ToString() + " [!]");
                } 
                else
                {
                    Console.WriteLine(json.ToString());
                    Console.WriteLine("[!] FAILED [!]");
                    await Browser.CloseAsync();
                    await SolveCaptcha();
                    return;
                }
            }
        }
    }
}
