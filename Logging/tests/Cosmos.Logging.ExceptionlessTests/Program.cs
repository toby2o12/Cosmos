﻿using System;
using Cosmos.Logging.Events;
using Cosmos.Logging.RunsOn.Console;
using Cosmos.Logging.RunsOn.Console.Settings;
using Cosmos.Logging.Settings;
using Cosmos.Logging.Sinks.Exceptionless;
using Exceptionless.Configuration;

namespace Cosmos.Logging.ExceptionlessTests {
    class Program {
        static void Main(string[] args) {
            try {
                LOGGER.Initialize().RunsOnConsole()
                    .WriteToExceptionless(s => s.UseXmlConfig("App.Config"))
                    .AllDone();

                var logger = LOGGER.GetLogger(mode: LogEventSendMode.Manually);

                logger.Information("hello", ctx => ctx.ForExceptionless(h => h.AddTags("CosmosLogging")));
                logger.Information("hello2", builder => builder.AddTags("CosmosLogging"));
                logger.Error("world");
                logger.SubmitLogger();

                Console.WriteLine("Hello World!");
            }
            catch (Exception e) {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.Source);
                Console.WriteLine(e.StackTrace);
            }

            Console.ReadLine();
        }
    }
}