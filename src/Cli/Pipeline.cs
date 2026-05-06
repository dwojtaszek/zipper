namespace Zipper.Cli;

public static class Pipeline
{
    public static FileGenerationRequest? Build(string[] args)
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
}
