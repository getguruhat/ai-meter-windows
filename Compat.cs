// The usings the .NET 8 Windows Forms SDK added on its own.
global using System;
global using System.Collections.Generic;
global using System.Drawing;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Windows.Forms;

namespace System.Runtime.CompilerServices;

/// Lets records and init-only properties compile for .NET Framework, which doesn't define it.
internal static class IsExternalInit { }
