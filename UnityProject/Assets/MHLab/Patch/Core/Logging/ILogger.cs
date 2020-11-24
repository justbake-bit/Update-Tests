using System;

namespace MHLab.Patch.Core.Logging
{
    public interface ILogger
    {
        void Debug(string messageTemplate, params object[] parameters);
        void Info(string messageTemplate, params object[] parameters);
        void Warning(string messageTemplate, params object[] parameters);
        void Error(Exception exception, string messageTemplate, params object[] parameters);
    }
}
