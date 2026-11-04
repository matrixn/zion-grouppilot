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
        var page = Page("Post Library", "Browse saved posts and choose one to edit, reuse or remove.");
        Action(page, "New draft", () => Edit(null), primary: true);
        var saved = model.Posts.Where(post => !post.IsArchived).ToArray();
        if (saved.Length == 0)
        {
            page.Children.Add(Label("No posts yet. Create a draft or import posts from Facebook."));
            return;
        }

        var cards = ResponsiveGrid(page, 3, 300);
        foreach (var post in saved)
        {
            var card = Section(cards, post.Title);
            card.Children.Add(CreatePostThumbnail(post));
            card.Children.Add(Label(post.Kind.ToString()));
            var text = Label(string.IsNullOrWhiteSpace(post.Body) ? "No text available." : post.Body);
            text.IsTextSelectionEnabled = true;
            text.MaxLines = 6;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            card.Children.Add(text);

            var actions = ResponsiveGrid(card, 2, 108);
            Action(actions, "Edit", () => Edit(post));
            Action(actions, "Remix", () => Edit(new Post
            {
                Kind = PostKind.Remix, Title = $"{post.Title} (remix)", Body = post.Body, Url = post.Url,
                ThumbnailUrl = post.ThumbnailUrl,
                Media = post.Media.Select(media => new PostMedia { Path = media.Path }).ToList(),
                Variants = post.Variants.Select(variant => new PostVariant { Body = variant.Body }).ToList()
            }));
            if (Validation.IsFacebookUrl(post.Url))
                Action(actions, "Open in Facebook", () => OpenFacebookAsync(post.Url));
            Action(actions, "Delete", () => DeleteLibraryPostAsync(post));
        }
    }

    private static Grid CreatePostThumbnail(Post post)
    {
        var panel = new Grid { Height = 168 };
        var placeholder = Label("No image available");
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        var image = new Image { Stretch = Stretch.UniformToFill };
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
                    var bitmap = new BitmapImage { DecodePixelWidth = 480 };
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
                image.Source = new BitmapImage(new Uri(post.ThumbnailUrl)) { DecodePixelWidth = 480 };
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
        if (editingPost?.Id == post.Id)
            editingPost = null;
        await model.ReloadAsync();
        ShowLibrary();
    }

    private Task Edit(Post? post)
    {
        editingPost = post;
        NavigateTo("Composer");
        return Task.CompletedTask;
    }

    private void ShowComposer(Post? source)
    {
        var page = Page("Post Composer", "Save a Facebook post by URL, write a new draft or edit a remix from the library.");
        var kind = Choices("Workflow", Enum.GetValues<PostKind>(), source?.Kind ?? PostKind.New);
        var title = Input("Title", source?.Title ?? "");
        var body = Input("Text or source description", source?.Body ?? "");
        body.AcceptsReturn = true;
        body.TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap;
        body.MinHeight = 110;
        var url = Input("Source URL or link", source?.Url ?? "");
        var variant = Input("Optional alternate text", source?.Variants.FirstOrDefault()?.Body ?? "");
        var paths = source?.Media.Select(media => media.Path).ToList() ?? [];
        var images = List(paths.ToArray());
        var details = Section(page, "Draft details");
        ResponsiveGrid(details, 2, 250, kind, title);
        details.Children.Add(body);
        ResponsiveGrid(details, 2, 250, url, variant);

        var media = Section(page, "Images");
        media.Children.Add(images);
        var mediaActions = ResponsiveGrid(media, 2, 185);
        Action(mediaActions, "Attach image", async () =>
        {
            var path = await PickFileAsync(".jpg", ".jpeg", ".png", ".webp", ".gif");
            if (path is not null && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(path);
                images.ItemsSource = paths.ToArray();
            }
        });
        Action(mediaActions, "Remove selected image", () =>
        {
            if (images.SelectedItem is not string path)
                throw new ArgumentException("Select an image to remove.");
            paths.Remove(path);
            images.ItemsSource = paths.ToArray();
            return Task.CompletedTask;
        });

        Post Draft() => new()
        {
            Id = source?.Id ?? 0, Kind = (PostKind)kind.SelectedItem, Title = title.Text.Trim(),
            Body = body.Text.Trim(), Url = url.Text.Trim(),
            ThumbnailUrl = source is not null && source.Url == url.Text.Trim() ? source.ThumbnailUrl : "",
            Media = paths.Select(path => new PostMedia { Path = path }).ToList(),
            Variants = string.IsNullOrWhiteSpace(variant.Text) ? [] : [new PostVariant { Body = variant.Text.Trim() }]
        };

        var draftActions = ResponsiveGrid(page, 2, 160);
        Action(draftActions, "Preview", async () =>
        {
            var post = Draft();
            Validation.CheckPost(post);
            var preview = new StackPanel { Spacing = 8 };
            preview.Children.Add(Label(post.Body));
            preview.Children.Add(Label(post.Url));
            foreach (var media in post.Media)
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(media.Path);
                    using var stream = await file.OpenReadAsync();
                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 480 };
                    await bitmap.SetSourceAsync(stream);
                    preview.Children.Add(new Image { Source = bitmap, MaxHeight = 180 });
                }
                catch
                {
                    preview.Children.Add(Label($"Image unavailable: {Path.GetFileName(media.Path)}"));
                }
            }
            var dialog = Dialog(post.Title, new ScrollViewer { Content = preview, MaxHeight = 400 }, "");
            dialog.CloseButtonText = "Close";
            await dialog.ShowAsync();
        });
        Action(draftActions, "Save draft", async () =>
        {
            var draft = Draft();
            Validation.CheckPost(draft);
            var mediaDirectory = Path.Combine(App.DataDirectory, "media");
            Directory.CreateDirectory(mediaDirectory);
            var copied = new List<string>();
            try
            {
                foreach (var media in draft.Media.Where(media => !Path.GetDirectoryName(media.Path)!.Equals(mediaDirectory, StringComparison.OrdinalIgnoreCase)))
                {
                    var destination = Path.Combine(mediaDirectory, Guid.NewGuid().ToString("N") + Path.GetExtension(media.Path));
                    await using var input = File.OpenRead(media.Path);
                    await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                    copied.Add(destination);
                    await input.CopyToAsync(output);
                    media.Path = destination;
                }
                await storage.SavePostAsync(draft);
            }
            catch
            {
                foreach (var path in copied)
                    File.Delete(path);
                throw;
            }
            await model.ReloadAsync();
            editingPost = null;
            NavigateTo("Library");
        }, "Draft saved.", primary: true);
    }
}
