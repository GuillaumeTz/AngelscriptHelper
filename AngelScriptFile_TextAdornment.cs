using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AngelScriptHelper
{
	/// <summary>
	/// TextAdornment1 places red boxes behind all the "a"s in the editor window
	/// </summary>
	internal sealed class AngelScriptFile_TextAdornment : IDisposable
	{
		private readonly IAdornmentLayer layer;
		private readonly IWpfTextView view;

		private readonly Brush SuccessBrush;

		private readonly string FilePath;
		private bool bDiagnosticDirty = false;

		private readonly object TagCompileSuccessObject = new object();
		private bool bHasTagCompileSuccess = false;

		/// <summary>
		/// Initializes a new instance of the <see cref="AngelScriptFile_TextAdornment"/> class.
		/// </summary>
		/// <param name="view">Text view to create the adornment for</param>
		public AngelScriptFile_TextAdornment(IWpfTextView view, string InFilePath)
		{
			FilePath = InFilePath;

			// Get the ITextBuffer from the IWpfTextView
			this.view = view;
			this.layer = this.view.GetAdornmentLayer("AngelScriptFile_TextAdornment");

			this.view.TextBuffer.Changed += OnTextBufferChanged;
			this.view.Closed += (sender, e) =>
			{
				this.Dispose();
			};

			this.SuccessBrush = new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0xff, 0x00));
			this.SuccessBrush.Freeze();

			CAngelScriptManager.Instance().OnDiagnosticsChanged += OnDiagnosticsChanged;
		}

		private void OnDiagnosticsChanged(object sender, EventArgs e)
		{
			if (bDiagnosticDirty)
				return;

			bDiagnosticDirty = true;

			ThreadHelper.JoinableTaskFactory.Run(async delegate
			{
				await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
				RefreshAll();
			});
		}

		// Implement IDisposable.
		// Do not make this method virtual.
		// A derived class should not be able to override this method.
		public void Dispose()
		{
			this.view.TextBuffer.Changed -= OnTextBufferChanged;
			CAngelScriptManager.Instance().OnDiagnosticsChanged -= OnDiagnosticsChanged;
			GC.SuppressFinalize(this);
		}

		private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
		{
			if (bHasTagCompileSuccess)
			{
				this.layer.RemoveAdornmentsByTag(TagCompileSuccessObject);
				bHasTagCompileSuccess = false;
			}
		}

		private void RefreshAll()
		{
			if (!bDiagnosticDirty)
				return;

			bDiagnosticDirty = false;
			if (bHasTagCompileSuccess)
			{
				this.layer.RemoveAdornmentsByTag(TagCompileSuccessObject);
				bHasTagCompileSuccess = false;
			}

			bool bHasMesssage = CAngelScriptManager.Instance().GetDiagnosticsMessageMap().TryGetValue(FilePath, out CDiagnosticsMessage DiagnosticMessage);
			if (!bHasMesssage)
				return;

			if (DiagnosticMessage.Diagnostics.Count == 0)
			{
				Rect viewportRect = new Rect(this.view.ViewportLeft, this.view.ViewportTop, this.view.ViewportWidth, this.view.ViewportHeight);
				Geometry viewportGeometry = new RectangleGeometry(viewportRect);

				if (viewportGeometry != null)
				{
					var drawing = new GeometryDrawing(this.SuccessBrush, null, viewportGeometry);
					drawing.Freeze();

					var drawingImage = new DrawingImage(drawing);
					drawingImage.Freeze();

					var image = new Image
					{
						Source = drawingImage,
					};

					// Align the image with the top of the bounds of the text geometry
					Canvas.SetLeft(image, viewportGeometry.Bounds.Left);
					Canvas.SetTop(image, viewportGeometry.Bounds.Top);

					this.layer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, TagCompileSuccessObject, image, null);
					bHasTagCompileSuccess = true;
				}
			}
		}
	}
}
