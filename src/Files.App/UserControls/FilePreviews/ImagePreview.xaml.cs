using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml.Controls;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Files.App.UserControls.FilePreviews
{
	public sealed partial class ImagePreview : UserControl
	{
		public ImagePreview(ImagePreviewViewModel model)
		{
			ViewModel = model;
			InitializeComponent();
			Loaded += (_, _) => ViewModel.SetPreviewSize(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight), XamlRoot.RasterizationScale);
			Unloaded += ViewModel.PreviewControlBase_Unloaded;
		}

		private ImagePreviewViewModel ViewModel { get; set; }
	}
}
