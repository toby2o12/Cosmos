﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Cosmos.Logging.Collectors;
using Cosmos.Logging.Core;
using Cosmos.Logging.Core.ObjectResolving;
using Cosmos.Logging.Events;
using Cosmos.Logging.MessageTemplates;

namespace Cosmos.Logging {
    public abstract partial class LoggerBase : ILogger {
        private readonly ILogPayloadSender _logPayloadSender;
        private readonly MessageParameterProcessor _messageParameterProcessor;

        protected LoggerBase(Type sourceType, LogEventLevel minimumLevel, string loggerName, LogEventSendMode sendMode, ILogPayloadSender logPayloadSender) {
            Name = loggerName;
            TargetType = sourceType ?? typeof(object);
            MinimumLevel = minimumLevel;
            SendMode = sendMode;
            _logPayloadSender = logPayloadSender ?? throw new ArgumentNullException(nameof(logPayloadSender));
            _messageParameterProcessor = MessageParameterProcessorCache.Get();

            AutomaticPayload = new LogPayload(sourceType, loggerName, Enumerable.Empty<LogEvent>());
            ManuallyPayload = new LogPayload(sourceType, loggerName, Enumerable.Empty<LogEvent>());
        }

        public string Name { get; }
        public Type TargetType { get; }
        public LogEventLevel MinimumLevel { get; }
        public LogEventSendMode SendMode { get; }

        private readonly ILogPayload AutomaticPayload;
        private readonly ILogPayload ManuallyPayload;

        public bool IsEnabled(LogEventLevel level) {
            if (MinimumLevel == LogEventLevel.Off)
                return true;

            if ((int) level >= (int) MinimumLevel)
                return true;

            return false;
        }

        protected virtual bool IsManuallySendMode(LogEvent logEvent) {
            return
                SendMode == LogEventSendMode.Customize && logEvent.SendMode == LogEventSendMode.Manually ||
                SendMode == LogEventSendMode.Manually;
        }

        public void Write(LogEventLevel level, Exception exception, string messageTemplate, LogEventSendMode sendMode,
            AdditionalOptContext context = null, params object[] messageTemplateParameters) {
            if (!IsEnabled(level)) return;
            if (string.IsNullOrWhiteSpace(messageTemplate)) return;

            _messageParameterProcessor.Process(messageTemplate, __as(messageTemplateParameters), out var parsedTemplate, out var parsedProperties);

            var logEvent = new LogEvent(DateTimeOffset.Now, level, parsedTemplate, exception, sendMode, parsedProperties, context);

            Dispatch(logEvent);

            object[] __as(object[] __paramObjs) {
                if (__paramObjs != null && __paramObjs.GetType() != typeof(object[]))
                    return new object[] {__paramObjs};
                return __paramObjs;
            }
        }

        public void Write(LogEvent logEvent) {
            if (logEvent == null) return;
            if (!IsEnabled(logEvent.Level)) return;
            Dispatch(logEvent);
        }

        protected virtual void Dispatch(LogEvent logEvent) {
            if (IsManuallySendMode(logEvent)) {
                ManuallyPayload.Add(logEvent);
            } else {
                AutomaticPayload.Add(logEvent);
                LogPayloadEmitter.Emit(_logPayloadSender, AutomaticPayload.Export());
            }
        }

        public void SubmitLogger() {
            LogPayloadEmitter.Emit(_logPayloadSender, ManuallyPayload.Export());
        }

        private static AdditionalOptContext TouchAdditionalOptContext(Action<AdditionalOptContext> additionalOptContextAct) {
            var context = new AdditionalOptContext();
            additionalOptContextAct?.Invoke(context);
            return context;
        }
    }
}