using GroupPilot.Core;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private void ShowLibrary()
    {
        var page = Page("Post Library", "Open a draft, create a remix or review the source on Facebook.");
        var posts = List(model.Posts);
        page.Children.Add(model.Posts.Count == 0 ? Label("No posts yet. Create a draft to get started.") : posts);
        Action(page, "New draft", () => Edit(null));
        Action(page, "Edit selected draft", () => Edit(SelectedPost(posts)));
        Action(page, "Remix selected post", () =>
        {
            var source = SelectedPost(posts);
            return Edit(new Post
            {
                Kind = PostKind.Remix, Title = $"{source.Title} (remix)", Body = source.Body, Url = source.Url,
                Media = source.Media.Select(media => new PostMedia { Path = media.Path }).ToList(),
                Variants = source.Variants.Select(variant => new PostVariant { Body = variant.Body }).ToList()
            });
        });
        Action(page, "Open Facebook post", () => OpenFacebookAsync(SelectedPost(posts).Url));
    }

    private static Post SelectedPost(ListView posts) => posts.SelectedItem as Post
        ?? throw new ArgumentException("Select a saved post first.");

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
        foreach (var control in new Microsoft.UI.Xaml.UIElement[] { kind, title, body, url, variant, images })
            page.Children.Add(control);

        Action(page, "Attach image", async () =>
        {
            var path = await PickFileAsync(".jpg", ".jpeg", ".png", ".webp", ".gif");
            if (path is not null && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(path);
                images.ItemsSource = paths.ToArray();
            }
        });
        Action(page, "Remove selected image", () =>
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
            Media = paths.Select(path => new PostMedia { Path = path }).ToList(),
            Variants = string.IsNullOrWhiteSpace(variant.Text) ? [] : [new PostVariant { Body = variant.Text.Trim() }]
        };

        Action(page, "Preview", async () =>
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
        Action(page, "Save draft", async () =>
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
        }, "Draft saved.");
    }
}
