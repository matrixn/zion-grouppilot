using GroupPilot.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private void ShowLibrary()
    {
        var page = Page("Post Library", "Browse imported Facebook posts for your group campaigns.");
        var saved = model.Posts.Where(post => IsLibraryPost(post)).ToArray();
        if (saved.Length == 0)
        {
            page.Children.Add(Label("No Facebook posts saved yet."));
            Action(page, "Open Facebook Browser", () => OpenPage("Browser"), primary: true);
            return;
        }

        var cards = ResponsiveGrid(page, 2, 350);
        foreach (var post in saved)
        {
            var card = Section(cards, post.Title);
            card.Children.Add(CreatePostThumbnail(post));
            var text = Label(string.IsNullOrWhiteSpace(post.Body) ? "No text available." : post.Body);
            text.IsTextSelectionEnabled = true;
            text.MaxLines = 6;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            card.Children.Add(text);

            var actions = ResponsiveGrid(card, 2, 108);
            Action(actions, "Open in Facebook", () => OpenFacebookAsync(post.Url));
            Action(actions, "Delete", () => DeleteLibraryPostAsync(post));
        }
    }

    private static bool IsLibraryPost(Post post) =>
        !post.IsArchived && post.Kind == PostKind.Existing && Validation.IsFacebookPostUrl(post.Url);

    private static Grid CreatePostThumbnail(Post post, double height = 270)
    {
        var panel = new Grid { Height = height };
        var placeholder = Label("No image available");
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        var image = new Image { Stretch = Stretch.Uniform };
        image.ImageOpened += (_, _) => placeholder.Visibility = Visibility.Collapsed;
        image.ImageFailed += (_, _) => placeholder.Visibility = Visibility.Visible;
        panel.Children.Add(placeholder);
        panel.Children.Add(image);

        if (post.Media.FirstOrDefault() is { } media)
        {
            image.Loaded += async (_, _) =>
            {
                if (image.Source is not null)
                    return;
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(media.Path);
                    using var stream = await file.OpenReadAsync();
                    var bitmap = new BitmapImage { DecodePixelWidth = 800 };
                    await bitmap.SetSourceAsync(stream);
                    image.Source = bitmap;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Debug(ex, "Post thumbnail unavailable for {PostId}", post.Id);
                    SetRemoteThumbnail();
                }
            };
        }
        else
        {
            SetRemoteThumbnail();
        }

        void SetRemoteThumbnail()
        {
            if (Validation.IsFacebookThumbnailUrl(post.ThumbnailUrl))
                image.Source = new BitmapImage(new Uri(post.ThumbnailUrl)) { DecodePixelWidth = 800 };
        }
        return panel;
    }

    private async Task DeleteLibraryPostAsync(Post post)
    {
        var inCampaign = model.Campaigns.Any(campaign => campaign.PostId == post.Id);
        var message = inCampaign
            ? "This post is used in a campaign. It will be removed from the library and retained for that campaign."
            : "This removes the saved copy from GroupPilot. The original Facebook post stays on Facebook.";
        if (await Dialog($"Delete {post.Title}?", Label(message), "Delete").ShowAsync() != ContentDialogResult.Primary)
            return;

        await storage.DeletePostFromLibraryAsync(post.Id);
        await model.ReloadAsync();
        ShowLibrary();
    }
}
