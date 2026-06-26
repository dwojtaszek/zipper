namespace Zipper.Cli;

public static class Pipeline
{
    public static FileGenerationRequest? Build(string[] args)
    {
        if (args is null || args.Length is 0)
        {
            HelpTextGenerator.Show();
            return null;
        }

        var parsedArgs = CliParser.Parse(args);
        if (parsedArgs is null)
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
