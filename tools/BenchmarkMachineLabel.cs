internal static class BenchmarkMachineLabel
{
    public static string Value
    {
        get
        {
            var label = Environment.GetEnvironmentVariable("SKECS_BENCHMARK_MACHINE_LABEL");
            if (label is null or { Length: < 1 or > 64 } || !label.All(IsAsciiLabelCharacter))
                return "local-machine";

            foreach (var identity in new[]
            {
                Environment.MachineName, Environment.UserName,
                Environment.GetEnvironmentVariable("COMPUTERNAME"), Environment.GetEnvironmentVariable("USERNAME")
            })
            {
                if (!string.IsNullOrEmpty(identity) && string.Equals(label, identity, StringComparison.OrdinalIgnoreCase))
                    return "local-machine";
            }
            return label;
        }
    }

    private static bool IsAsciiLabelCharacter(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';
}
