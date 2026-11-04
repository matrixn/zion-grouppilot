(request => {
    // Operates only on visible Facebook controls. No network calls, cookies or credentials.
    const result = (status, message = '', url = '') => ({ status, message, url });
    const visible = element => element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden';
    const text = element => (element.innerText || element.textContent || '').trim();
    const name = element => (element.getAttribute('aria-label') || text(element)).trim();
    const facebook = url => url.protocol === 'https:' && !url.username && !url.password &&
        (!url.port || url.port === '443') && (url.hostname === 'facebook.com' || url.hostname.endsWith('.facebook.com'));
    const group = value => {
        try {
            const url = new URL(value, location.href);
            return facebook(url) ? url.pathname.match(/^\/groups\/([a-z0-9._-]+)(?:\/|$)/i)?.[1].toLowerCase() : null;
        } catch { return null; }
    };
    const expectedGroup = group(request.groupUrl);
    const source = new URL(request.postUrl);
    if (!facebook(source) || !expectedGroup || group(location.href) !== expectedGroup)
        return result('wrong-page', 'Facebook is not showing the selected group.');
    if (document.querySelector('input[type="password"]') ||
        Array.from(document.querySelectorAll('iframe[src*="captcha"], [data-sitekey]')).some(visible))
        return result('authentication', 'Facebook requires sign-in or a security check.');
    const dialogs = Array.from(document.querySelectorAll('[role="dialog"]')).filter(visible);
    const signals = () => Array.from(document.querySelectorAll('[role="alert"], [role="status"], [aria-live="polite"], [aria-live="assertive"]'))
        .filter(visible).filter(element => text(element).length > 0 && text(element).length < 1200);
    const restricted = /temporarily blocked|you can't use this feature|going too fast|account.{0,30}restricted|blocat.{0,6}temporar|nu po[tț]i folosi aceast[ăa] func[tț]ie|prea repede|contul.{0,30}restric/i;
    if ([...signals(), ...dialogs].some(element => restricted.test(text(element))))
        return result('restricted', 'Facebook reported a restriction. The campaign has stopped.');
    const postLabel = /^(post|publish|public[ăa]|posteaz[ăa])$/i;
    const headingLabel = /^(create post|create a post|creeaz[ăa] o postare|creare postare)$/i;
    const editorSelector = '[contenteditable="true"][role="textbox"], [contenteditable="true"][data-lexical-editor="true"]';
    const buttons = scope => Array.from(scope.querySelectorAll('[role="button"],button')).filter(visible);
    const leaf = elements => elements.filter(element => !elements.some(other => element !== other && element.contains(other)));
    const editors = scope => Array.from(scope.querySelectorAll(editorSelector)).filter(visible);
    const composers = leaf(dialogs.filter(dialog => editors(dialog).length > 0 &&
        (buttons(dialog).some(button => postLabel.test(name(button))) ||
            Array.from(dialog.querySelectorAll('[role="heading"],h1,h2')).some(heading => visible(heading) && headingLabel.test(text(heading))))));
    const samePost = value => {
        try {
            const url = new URL(value, location.href);
            return facebook(url) && url.pathname.replace(/\/$/, '') === source.pathname.replace(/\/$/, '') &&
                ['story_fbid', 'fbid', 'v', 'id'].every(key => !source.searchParams.has(key) || url.searchParams.get(key) === source.searchParams.get(key));
        } catch { return false; }
    };
    const preview = scope => Array.from(scope.querySelectorAll('a[href]')).some(link => visible(link) && samePost(link.href));
    const articles = () => leaf(Array.from(document.querySelectorAll('[role="article"],article')).filter(visible));
    const permalinkUrl = value => {
        try {
            const url = new URL(value, location.href);
            return group(url.href) === expectedGroup && /^\/groups\/[^/]+\/(posts|permalink)\/[^/]+\/?$/i.test(url.pathname)
                && !url.searchParams.has('comment_id') && !url.searchParams.has('reply_comment_id')
                ? url.origin + url.pathname : '';
        } catch { return ''; }
    };
    const groupPermalink = scope => Array.from(scope.querySelectorAll('a[href]')).map(link => permalinkUrl(link.href)).find(Boolean) || '';
    let state = window.__groupPilotPublication;
    if (request.operation === 'observe') {
        if (!state || state.token !== request.token || !state.submitted)
            return result('uncertain', 'The submission context was lost. Check Facebook before retrying.');
        const fresh = [...signals(), ...dialogs].filter(element => !state.previousSignals.has(text(element)));
        const pending = /(?:your post|the post|postarea|postarea ta).{0,60}(?:submitted for approval|pending approval|awaiting approval|în așteptarea aprobării|trimis[ăa] spre aprobare)|^(?:submitted for approval|post pending approval)/i;
        const success = /(?:your post|the post).{0,40}(?:was published|has been published|is now published|was posted|has been posted)|^post (?:published|posted)|postarea (?:ta )?(?:a fost|este) publicat[ăa]|^ai publicat o postare/i;
        const failure = /couldn't (?:share|publish|post)|could not (?:share|publish|post)|something went wrong|nu s-a putut (?:publica|distribui)|a ap[ăa]rut o eroare/i;
        if (fresh.some(element => pending.test(text(element))))
            return result('approval', 'Facebook confirmed that the post was submitted for group approval.');
        if (fresh.some(element => failure.test(text(element))))
            return result('uncertain', 'Facebook reported an error after submission. Check the group before retrying.');
        const publishedArticle = articles().find(article => !state.previousArticles.has(article) && preview(article) &&
            groupPermalink(article) && !state.previousUrls.has(groupPermalink(article)));
        // A fresh, explicit Facebook confirmation is required; dialog disappearance is not proof.
        if (fresh.some(element => success.test(text(element))))
            return result('published', 'Facebook displayed a new publication confirmation.', publishedArticle ? groupPermalink(publishedArticle) : '');
        const recent = publishedArticle && Array.from(publishedArticle.querySelectorAll('time,a[href]')).some(element =>
            (element.tagName === 'TIME' || permalinkUrl(element.href)) &&
            /^(just now|(?:a few|\d+) seconds? ago|acum|acum (?:câteva|\d+) secunde)$/i.test(name(element)));
        if (recent && composers.length === 0)
            return result('published', 'Facebook displayed a new group post with the selected source link and a current timestamp.', groupPermalink(publishedArticle));
        return result('waiting', 'Waiting for Facebook to confirm the result…');
    }
    if (state?.submitted)
        return result('uncertain', 'A submission already started in this document. Check its outcome before retrying.');
    if (composers.length === 1) {
        const composer = composers[0];
        const fields = editors(composer);
        if (fields.length !== 1)
            return result('ambiguous', 'The Facebook editor could not be identified uniquely.');
        const editor = fields[0];
        const current = text(editor);
        if (current.length > 0 && current !== request.postUrl)
            return result('occupied', 'The Facebook editor already contains different text. It was left unchanged.');
        if (request.operation === 'submit') {
            if (!state || state.token !== request.token || state.postUrl !== request.postUrl || state.groupUrl !== request.groupUrl ||
                (current !== request.postUrl && !preview(composer)))
                return result('uncertain', 'The editor changed before submission. Nothing further was clicked.');
            const submit = leaf(buttons(composer).filter(button => postLabel.test(name(button))));
            if (submit.length !== 1)
                return result('ambiguous', 'The Publish button could not be identified uniquely.');
            if (submit[0].disabled || submit[0].getAttribute('aria-disabled') === 'true')
                return result('waiting', 'Waiting for Facebook to enable Publish…');
            state.previousSignals = new Set([...signals(), ...dialogs].map(text));
            state.previousArticles = new Set(articles());
            state.previousUrls = new Set(articles().map(groupPermalink).filter(Boolean));
            state.submitted = true; // Set before clicking: this document must never click twice.
            submit[0].click();
            return result('submitted', 'Submitted once. Waiting for confirmation…');
        }
        if (current === request.postUrl || preview(composer)) {
            state = window.__groupPilotPublication = {
                token: request.token, postUrl: request.postUrl, groupUrl: request.groupUrl, submitted: false
            };
            const submit = leaf(buttons(composer).filter(button => postLabel.test(name(button))));
            return submit.length === 1 && !submit[0].disabled && submit[0].getAttribute('aria-disabled') !== 'true'
                ? result('ready') : result('waiting');
        }
        if (editor.dataset.groupPilotUrl === request.postUrl)
            return result('waiting');
        editor.focus();
        editor.dataset.groupPilotUrl = request.postUrl;
        document.execCommand('insertText', false, request.postUrl);
        return result('waiting');
    }
    if (request.operation === 'submit')
        return result('uncertain', 'The editor disappeared before submission. Check Facebook before retrying.');
    if (dialogs.length > 0)
        return result('ambiguous', 'An unexpected Facebook dialog needs attention.');
    const labels = /^(write something|scrie ceva|scrie o postare|posteaz[ăa] ceva|create a post|creeaz[ăa] o postare)([.\s…]|$)/i;
    const compose = leaf(buttons(document.querySelector('[role="main"]') || document).filter(button => labels.test(name(button))));
    if (compose.length === 1) {
        compose[0].click();
        return result('opening');
    }
    return result('loading');
})
