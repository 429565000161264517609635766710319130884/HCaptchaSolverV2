using HCaptcha_SolverV2.API;
using System;
using System.Threading.Tasks;

namespace HCaptcha_SolverV2
{
    class Program
    {
        static void Main(string[] args)
        {
            new Program().Start().GetAwaiter().GetResult();
        }

        public async Task Start()
        {
            Console.Title = "H";
            HCaptcha captcha = new HCaptcha("https://dashboard.hcaptcha.com/signup", false, true);
            await captcha.SolveCaptcha();
            Console.ReadKey();
        }
    }
}
