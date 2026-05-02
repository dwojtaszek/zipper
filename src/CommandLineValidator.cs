using Zipper.Cli;

namespace Zipper
{
    public static class CommandLineValidator
    {
        public static FileGenerationRequest? ValidateAndParseArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                HelpTextGenerator.Show();
                return null;
            }

            var parsedArgs = CliParser.Parse(args);
            if (parsedArgs == null)
            {
                return null;
            }

            if (!CliValidator.Validate(parsedArgs))
            {
                return null;
            }

            return RequestBuilder.Build(parsedArgs);
        }

        public static void ShowUsage()
        {
            HelpTextGenerator.Show();
        }
    }
}
