using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using System.Threading;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using EnvDTE;
using EnvDTE80;

namespace AngelScriptHelper
{
	class CErrorListHelper
	{
		private readonly ErrorListProvider _errorListProvider;

		private static CErrorListHelper mInstance = null;

		public CErrorListHelper(IServiceProvider serviceProvider)
		{
			_errorListProvider = new ErrorListProvider(serviceProvider);
		}

		public static void Init(IServiceProvider ServiceProvider)
		{	
			if (mInstance == null)
				mInstance = new CErrorListHelper(ServiceProvider);
		}

		public static CErrorListHelper Instance()
		{
			return mInstance;
		}

		public void ShowErrorList()
		{
			_errorListProvider.Show();
		}

		public void AddError(string message, string filePath, int line, int column)
		{
			// Create a new error task item
			ErrorTask errorTask = new ErrorTask
			{
				Category = TaskCategory.BuildCompile,  // Or TaskCategory.CodeSense for real-time analysis
				ErrorCategory = TaskErrorCategory.Error, // Can be Warning or Message
				Text = message,
				Document = filePath,
				Line = line - 1,  // Line index is zero-based
				Column = column - 1
			};

			// Set navigation action when the user double-clicks the error
			errorTask.Navigate += (s, e) =>
			{
				ThreadHelper.ThrowIfNotOnUIThread();
				NavigateToFile(filePath, line, column);
			};

			// Add the error task to the provider
			_errorListProvider.Tasks.Add(errorTask);
		}

		public void ClearErrors()
		{
			_errorListProvider.Tasks.Clear();
		}

		private void NavigateToFile(string filePath, int line, int column)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			// Ensure file exists
			if (!System.IO.File.Exists(filePath))
				return;

			// Open the document in Visual Studio
			IVsUIHierarchy hierarchy;
			uint itemID;
			IVsWindowFrame windowFrame;
			VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, filePath, Guid.Empty, out hierarchy, out itemID, out windowFrame);

			if (windowFrame != null)
			{
				windowFrame.Show(); // Bring the document to the foreground

				// Get the IVsTextView to move the cursor
				MoveCursorWithDTE(filePath, line, column);
			}
		}

		private static void MoveCursorWithDTE(string filePath, int line, int column)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			DTE2 dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
			if (dte == null || dte.Documents == null)
				return;

			// Get the opened document
			try
			{
				Document doc = dte.Documents.Item(filePath);
				if (doc != null)
				{
					TextSelection selection = (TextSelection)doc.Selection;
					selection.MoveToLineAndOffset(line, column);
				}
			}
			catch (Exception)
			{

			}
		}
	}
}