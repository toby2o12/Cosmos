﻿using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Cosmos.Logging.Future;

namespace Cosmos.Logging.RunsOn.Console {
    public class ConsoleFutureLogger : FutureLoggerBase {

        [SuppressMessage("ReSharper", "ExplicitCallerInfoArgument")]
        public ConsoleFutureLogger(ILogger logger, [CallerMemberName] string memberName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
            : base(logger, memberName, filePath, lineNumber) { }
    }
}