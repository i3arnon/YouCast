using System;
using System.Diagnostics;

namespace Service
{
    public static class ExceptionHandler
    {
        public static void Handle(Exception exception) =>
            Debug.WriteLine(exception);
    }
}