(request => {
    // Observe the DOM and label uniquely identified controls. All input/clicks are performed by Playwright.
    const visible = el => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden';
    const text = el => (el.innerText || el.textContent || '').trim();
    const name = el => (el.getAttribute('aria-label') || text(el)).trim();
    const facebook = url => url.protocol === 'https:' && !url.username && !url.password && (!url.port || url.port === '443') &&
        (url.hostname === 'facebook.com' || url.hostname.endsWith('.facebook.com'));
    const group = value => {
        try { const url = new URL(value, location.href); return facebook(url) ? url.pathname.match(/^\/groups\/([a-z0-9._-]+)(?:\/|$)/i)?.[1].toLowerCase() : null; }
        catch { return null; }
    };
    const expectedGroup = group(request.groupUrl);
    const source = new URL(request.postUrl);
    if (!facebook(source) || !expectedGroup || group(location.href) !== expectedGroup)
        return { Status: 'wrong-page', Message: 'The browser is not showing the selected group.' };
    const dialogs = Array.from(document.querySelectorAll('[role="dialog"]')).filter(visible);
    const notices = Array.from(document.querySelectorAll('[role="alert"],[role="status"],[aria-live="polite"],[aria-live="assertive"]'))
        .filter(visible).map(text).filter(value => value.length > 0 && value.length <= 1200);
    const dialogText = dialogs.map(text).filter(value => value.length <= 1200);
    const restricted = /temporarily blocked|you can't use this feature|going too fast|account.{0,30}restricted|blocat.{0,6}temporar|nu po[tț]i folosi aceast[ăa] func[tț]ie|prea repede|contul.{0,30}restric/i;
    if (Array.from(document.querySelectorAll('input[type="password"],iframe[src*="captcha"],[data-sitekey]')).some(visible))
        return { Status: 'authentication', Message: 'Facebook requires sign-in or a security check.' };
    if ([...notices, ...dialogText].some(value => restricted.test(value)))
        return { Status: 'restricted', Message: 'Facebook reported a restriction.' };
    const postLabel = /^(post|publish|public[ăa]|posteaz[ăa])$/i;
    const composeLabel = /^(write something|write a (?:public )?post|scrie ceva|scrie o postare|posteaz[ăa] ceva|create (?:a )?post|creeaz[ăa] o postare)([.\s…]|$)/i;
    const editorSelector = '[contenteditable="true"][role="textbox"],[contenteditable="true"][data-lexical-editor="true"],textarea[aria-label]';
    const editors = scope => Array.from(scope.querySelectorAll(editorSelector)).filter(visible);
    const buttons = scope => Array.from(scope.querySelectorAll('[role="button"],button')).filter(visible);
    const leaf = nodes => nodes.filter(node => !nodes.some(other => node !== other && node.contains(other)));
    let composers = leaf(dialogs.filter(dialog => editors(dialog).length > 0 && buttons(dialog).some(button => postLabel.test(name(button)))));
    if (dialogs.length === 0 && composers.length === 0) {
        const fields = editors(document.querySelector('[role="main"],main') || document);
        if (fields.length === 1) {
            for (let scope = fields[0].parentElement, depth = 0; scope && depth < 7; scope = scope.parentElement, depth++) {
                if (buttons(scope).some(button => postLabel.test(name(button)))) { composers = [scope]; break; }
            }
        }
    }
    const sameSource = value => {
        try {
            let url = new URL(value, location.href);
            if (facebook(url) && url.pathname === '/l.php' && url.searchParams.has('u')) url = new URL(url.searchParams.get('u'));
            return facebook(url) && url.pathname.replace(/\/$/, '') === source.pathname.replace(/\/$/, '') &&
                ['story_fbid','fbid','v','id'].every(key => !source.searchParams.has(key) || url.searchParams.get(key) === source.searchParams.get(key));
        } catch { return false; }
    };
    const hasSource = scope => Array.from(scope.querySelectorAll('a[href]')).some(link => sameSource(link.href)) || text(scope).includes(request.postUrl);
    const permalink = value => {
        try {
            const url = new URL(value, location.href);
            return group(url.href) === expectedGroup && /^\/groups\/[^/]+\/(posts|permalink)\/[^/]+\/?$/i.test(url.pathname) &&
                !url.searchParams.has('comment_id') && !url.searchParams.has('reply_comment_id') ? url.origin + url.pathname : '';
        } catch { return ''; }
    };
    const articleSelector = '[role="article"],article,[data-pagelet^="FeedUnit_"]';
    const articles = Array.from(document.querySelectorAll(articleSelector)).filter(visible)
        .filter(article => !article.parentElement?.closest(articleSelector)).map(article => {
            const links = Array.from(article.querySelectorAll('a[href]'));
            const link = links.find(link => permalink(link.href));
            let timestamp = null;
            for (const element of article.querySelectorAll('time[datetime],[data-utime]')) {
                const value = element.getAttribute('data-utime');
                const parsed = value ? Number(value) * 1000 : Date.parse(element.getAttribute('datetime'));
                if (Number.isFinite(parsed)) { timestamp = parsed; break; }
            }
            if (timestamp === null && link) {
                const label = name(link).toLowerCase();
                const seconds = label.match(/^(\d+) (?:seconds? ago|secunde)$/);
                const minutes = label.match(/^(\d+) (?:minutes? ago|minute)$/);
                if (/^(just now|acum|a few seconds ago|acum câteva secunde)$/.test(label)) timestamp = Date.now();
                else if (seconds) timestamp = Date.now() - Number(seconds[1]) * 1000;
                else if (minutes) timestamp = Date.now() - Number(minutes[1]) * 60000;
            }
            return { Url: link ? permalink(link.href) : '', HasSource: hasSource(article), Timestamp: timestamp };
        }).filter(article => article.Url);
    const entry = leaf(buttons(document.querySelector('[role="main"],main') || document).filter(button => composeLabel.test(name(button))));
    const marker = request.token;
    for (const kind of ['composer','editor','submit','entry'])
        document.querySelectorAll('[data-grouppilot-' + kind + ']').forEach(el => el.removeAttribute('data-grouppilot-' + kind));
    entry.forEach(el => el.setAttribute('data-grouppilot-entry', marker));
    let editorText = '', hasPreview = false, editorCount = 0, submitCount = 0;
    if (composers.length === 1) {
        const composer = composers[0], fields = editors(composer), submit = leaf(buttons(composer).filter(button => postLabel.test(name(button))));
        composer.setAttribute('data-grouppilot-composer', marker);
        editorCount = fields.length; submitCount = submit.length;
        if (fields.length === 1) {
            fields[0].setAttribute('data-grouppilot-editor', marker);
            editorText = fields[0].tagName === 'TEXTAREA' ? fields[0].value.trim() : text(fields[0]);
        }
        if (submit.length === 1) submit[0].setAttribute('data-grouppilot-submit', marker);
        hasPreview = Array.from(composer.querySelectorAll('a[href]')).some(link => sameSource(link.href));
    }
    return { Status: 'ok', Message: '', ComposerCount: composers.length, EntryCount: entry.length, EditorCount: editorCount,
        SubmitCount: submitCount, EditorText: editorText, HasSource: hasPreview, Notices: [...new Set([...notices, ...dialogText])],
        Articles: articles, DialogCount: dialogs.length };
})
