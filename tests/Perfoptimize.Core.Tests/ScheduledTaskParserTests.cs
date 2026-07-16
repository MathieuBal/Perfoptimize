using Perfoptimize.Core.Startup;

namespace Perfoptimize.Core.Tests;

public class ScheduledTaskParserTests
{
    [Fact]
    public void Parses_author_command_and_arguments_from_an_exec_task()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>NVIDIA Corporation</Author>
              </RegistrationInfo>
              <Actions Context="Author">
                <Exec>
                  <Command>C:\Program Files\NVIDIA Corporation\nvupdater.exe</Command>
                  <Arguments>/silent /check</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        var info = ScheduledTaskParser.Parse(xml);

        Assert.NotNull(info);
        Assert.Equal("NVIDIA Corporation", info!.Author);
        Assert.Equal(@"C:\Program Files\NVIDIA Corporation\nvupdater.exe", info.Command);
        Assert.Equal("/silent /check", info.Arguments);
    }

    [Fact]
    public void Returns_null_for_a_task_without_an_exec_action()
    {
        // A COM-handler task has no command line to attribute.
        const string xml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Actions Context="Author">
                <ComHandler><ClassId>{00000000-0000-0000-0000-000000000000}</ClassId></ComHandler>
              </Actions>
            </Task>
            """;

        Assert.Null(ScheduledTaskParser.Parse(xml));
    }

    [Fact]
    public void Returns_null_for_garbage()
    {
        Assert.Null(ScheduledTaskParser.Parse("not xml at all"));
        Assert.Null(ScheduledTaskParser.Parse(""));
    }
}
