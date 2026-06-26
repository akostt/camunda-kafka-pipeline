using System.Text;

namespace OrderManager.Middleware;

/// <summary>
/// HTTP Basic Auth + cookie-сессия.
/// Credentials берутся из appsettings.json: Auth:Username / Auth:Password.
/// Первый успешный логин выставляет cookie "om_auth" (HttpOnly, SameSite=Strict).
/// </summary>
public sealed class BasicAuthMiddleware
{
    private const string CookieName = "om_auth";
    private const string LoginPath  = "/login";

    private readonly RequestDelegate _next;
    private readonly string _username;
    private readonly string _password;

    public BasicAuthMiddleware(RequestDelegate next, IConfiguration cfg)
    {
        _next     = next;
        _username = cfg["Auth:Username"] ?? "admin";
        _password = cfg["Auth:Password"] ?? throw new InvalidOperationException(
            "Auth:Password не задан в конфигурации. Укажите его в appsettings.json или переменной среды Auth__Password.");
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";

        // Выход из системы
        if (path.StartsWith("/logout", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Cookies.Delete(CookieName, new CookieOptions
            {
                Path     = "/",
                SameSite = SameSiteMode.Strict,
                Secure   = false,
            });
            ctx.Response.Redirect(LoginPath);
            return;
        }

        if (path.StartsWith(LoginPath, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase))
        {
            await HandleLoginRoute(ctx);
            return;
        }

        // Проверка куки
        if (ctx.Request.Cookies.TryGetValue(CookieName, out var token) && ValidateToken(token))
        {
            await _next(ctx);
            return;
        }

        // Проверка Basic-заголовка (для программных клиентов)
        if (TryParseBasicAuth(ctx.Request, out var user, out var pass) && Verify(user, pass))
        {
            IssueCookie(ctx);
            await _next(ctx);
            return;
        }

        if (AcceptsHtml(ctx.Request))
        {
            ctx.Response.Redirect($"{LoginPath}?returnUrl={Uri.EscapeDataString(ctx.Request.Path)}");
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"Askona Order Manager\"";
        await ctx.Response.WriteAsync("Требуется авторизация");
    }

    private async Task HandleLoginRoute(HttpContext ctx)
    {
        if (ctx.Request.Method == HttpMethods.Post)
        {
            var form = await ctx.Request.ReadFormAsync();
            var user = form["username"].ToString();
            var pass = form["password"].ToString();
            if (Verify(user, pass))
            {
                IssueCookie(ctx);
                var returnUrl = ctx.Request.Query["returnUrl"].ToString();
                ctx.Response.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
                return;
            }
            await WriteLoginPage(ctx, error: "Неверный логин или пароль. Проверьте введённые данные и попробуйте снова.");
        }
        else
        {
            if (ctx.Request.Cookies.TryGetValue(CookieName, out var t) && ValidateToken(t))
            {
                ctx.Response.Redirect("/");
                return;
            }
            await WriteLoginPage(ctx);
        }
    }

    private bool Verify(string user, string pass) =>
        string.Equals(user, _username, StringComparison.Ordinal) &&
        string.Equals(pass, _password, StringComparison.Ordinal);

    private string MakeToken() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}:{DateTime.UtcNow:yyyyMMdd}"));

    private bool ValidateToken(string token)
    {
        try
        {
            var raw   = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = raw.Split(':');
            return parts.Length >= 3 &&
                   string.Equals(parts[0], _username, StringComparison.Ordinal) &&
                   string.Equals(parts[1], _password, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private void IssueCookie(HttpContext ctx)
    {
        ctx.Response.Cookies.Append(CookieName, MakeToken(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure   = false,
            Expires  = DateTimeOffset.UtcNow.AddDays(1),
            Path     = "/",
        });
    }

    private static bool TryParseBasicAuth(HttpRequest req, out string user, out string pass)
    {
        user = pass = "";
        var auth = req.Headers.Authorization.ToString();
        if (!auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..]));
            var colon   = decoded.IndexOf(':');
            if (colon < 0) return false;
            user = decoded[..colon];
            pass = decoded[(colon + 1)..];
            return true;
        }
        catch { return false; }
    }

    private static bool AcceptsHtml(HttpRequest req) =>
        req.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    private static Task WriteLoginPage(HttpContext ctx, string? error = null)
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        var errHtml = error is null ? "" :
            $"<div class=\"alert\"><svg viewBox=\"0 0 20 20\" fill=\"currentColor\" width=\"18\" height=\"18\" style=\"flex-shrink:0\"><path fill-rule=\"evenodd\" d=\"M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z\" clip-rule=\"evenodd\"/></svg><span>{System.Net.WebUtility.HtmlEncode(error)}</span></div>";
        var html = $$"""
            <!DOCTYPE html>
            <html lang="ru">
            <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Askona · Вход в систему</title>
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link href="https://fonts.googleapis.com/css2?family=Lato:wght@300;400;700;900&family=Roboto+Mono:wght@400&display=swap" rel="stylesheet">
            <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0 }
            html, body { height: 100%; overflow: hidden; font-family: 'Lato', sans-serif }

            /* ── Split layout ── */
            .layout { display: flex; height: 100vh }

            /* ── Left: brand panel ── */
            .panel-brand {
              flex: 0 0 480px;
              background: linear-gradient(150deg, #2bb5a0 0%, #1d8c7a 55%, #155f56 100%);
              display: flex; flex-direction: column; justify-content: space-between;
              padding: 48px 52px; position: relative; overflow: hidden;
            }
            /* dot-grid overlay */
            .panel-brand::before {
              content: ''; position: absolute; inset: 0;
              background-image: radial-gradient(circle, rgba(255,255,255,.10) 1.5px, transparent 1.5px);
              background-size: 28px 28px;
            }
            /* big decorative logo */
            .deco-logo {
              position: absolute; right: -80px; bottom: -80px;
              opacity: .07; pointer-events: none; transform: rotate(-8deg);
            }

            .brand-top { position: relative; z-index: 1 }
            .brand-row  { display: flex; align-items: center; gap: 14px; margin-bottom: 56px }
            .brand-text .name { font-size: 26px; font-weight: 900; color: white; letter-spacing: -.02em; line-height: 1 }
            .brand-text .sub  { font-size: 9px; color: rgba(255,255,255,.6); letter-spacing: .18em; text-transform: uppercase; margin-top: 3px }
            .brand-hdivider   { width: 1px; height: 32px; background: rgba(255,255,255,.25) }
            .brand-module     { font-size: 13px; font-weight: 700; color: rgba(255,255,255,.85); letter-spacing: .03em }

            .brand-headline {
              font-size: 36px; font-weight: 900; color: white; line-height: 1.2;
              letter-spacing: -.02em; margin-bottom: 18px;
            }
            .brand-desc { font-size: 14px; color: rgba(255,255,255,.7); line-height: 1.8; max-width: 340px }

            .brand-bottom { position: relative; z-index: 1 }
            .brand-pills  { display: flex; gap: 10px; flex-wrap: wrap }
            .pill {
              background: rgba(255,255,255,.12); border: 1px solid rgba(255,255,255,.2);
              border-radius: 20px; padding: 6px 14px;
              font-size: 12px; font-weight: 700; color: rgba(255,255,255,.85);
              letter-spacing: .04em;
            }

            /* ── Right: form panel ── */
            .panel-form {
              flex: 1; display: flex; flex-direction: column;
              align-items: center; justify-content: center;
              background: #f7f8f9; padding: 48px;
            }
            .form-wrap { width: 100%; max-width: 380px }

            .form-eyebrow {
              font-size: 11px; font-weight: 900; letter-spacing: .12em;
              text-transform: uppercase; color: #2bb5a0; margin-bottom: 10px;
            }
            .form-title { font-size: 28px; font-weight: 900; color: #1a202c; margin-bottom: 8px }
            .form-sub { font-size: 14px; color: #9aa5b0; margin-bottom: 36px; line-height: 1.5 }

            /* alert */
            .alert {
              display: flex; align-items: center; gap: 10px;
              background: #fff5f5; border: 1.5px solid #fed7d7;
              color: #c53030; border-radius: 10px;
              padding: 12px 14px; font-size: 13px; font-weight: 700;
              margin-bottom: 22px; line-height: 1.4;
            }

            /* fields */
            .field { margin-bottom: 20px }
            .field-lbl {
              display: block; font-size: 11px; font-weight: 900;
              color: #677585; letter-spacing: .08em; text-transform: uppercase;
              margin-bottom: 7px;
            }
            .field-wrap { position: relative }
            .field-ico {
              position: absolute; left: 14px; top: 50%; transform: translateY(-50%);
              color: #9aa5b0; display: flex; pointer-events: none;
            }
            .inp {
              width: 100%; background: white;
              border: 1.5px solid #dde1e5; border-radius: 10px;
              padding: 12px 16px 12px 42px;
              font-size: 14px; font-family: 'Lato', sans-serif;
              color: #2d3748; outline: none;
              transition: border-color .15s, box-shadow .15s;
              box-shadow: 0 1px 3px rgba(0,0,0,.04);
            }
            .inp::placeholder { color: #c5ccd4 }
            .inp:focus {
              border-color: #2bb5a0;
              box-shadow: 0 0 0 3px rgba(43,181,160,.14), 0 1px 3px rgba(0,0,0,.04);
            }

            .btn {
              width: 100%; background: #2bb5a0; color: white;
              border: none; border-radius: 10px; padding: 14px;
              font-size: 15px; font-weight: 700; font-family: 'Lato', sans-serif;
              cursor: pointer; margin-top: 6px;
              transition: background .15s, transform .1s, box-shadow .15s;
              box-shadow: 0 4px 14px rgba(43,181,160,.38);
              letter-spacing: .01em;
            }
            .btn:hover {
              background: #26a08d; transform: translateY(-1px);
              box-shadow: 0 6px 20px rgba(43,181,160,.46);
            }
            .btn:active { transform: none; box-shadow: 0 2px 6px rgba(43,181,160,.3) }

            .form-foot {
              margin-top: 32px; padding-top: 24px;
              border-top: 1px solid #eef0f2;
              font-size: 12px; color: #9aa5b0; text-align: center;
            }

            @media (max-width: 800px) {
              .panel-brand { display: none }
              .panel-form { padding: 32px 24px }
            }
            </style>
            </head>
            <body>
            <div class="layout">

              <!-- ══ Brand panel (left) ══ -->
              <div class="panel-brand">
                <svg class="deco-logo" viewBox="0 0 192 192" xmlns="http://www.w3.org/2000/svg" width="520" height="520">
                  <g fill="white">
                    <circle cx="88.5"  cy="31.5"  r="11.9"/>
                    <circle cx="134.3" cy="50.7"  r="16.1"/>
                    <circle cx="42.0"  cy="50.7"  r="8.6"/>
                    <circle cx="153.0" cy="95.2"  r="21.3"/>
                    <circle cx="23.6"  cy="95.2"  r="7.0"/>
                    <circle cx="134.3" cy="140.2" r="16.1"/>
                    <circle cx="42.1"  cy="140.2" r="8.6"/>
                    <circle cx="88.5"  cy="159.4" r="11.9"/>
                  </g>
                </svg>

                <div class="brand-top">
                  <div class="brand-row">
                    <svg viewBox="0 0 192 192" xmlns="http://www.w3.org/2000/svg" width="44" height="44">
                      <g fill="white">
                        <circle cx="88.5"  cy="31.5"  r="11.9"/>
                        <circle cx="134.3" cy="50.7"  r="16.1"/>
                        <circle cx="42.0"  cy="50.7"  r="8.6"/>
                        <circle cx="153.0" cy="95.2"  r="21.3"/>
                        <circle cx="23.6"  cy="95.2"  r="7.0"/>
                        <circle cx="134.3" cy="140.2" r="16.1"/>
                        <circle cx="42.1"  cy="140.2" r="8.6"/>
                        <circle cx="88.5"  cy="159.4" r="11.9"/>
                      </g>
                    </svg>
                    <div class="brand-text">
                      <div class="name">askona</div>
                      <div class="sub">территория здорового сна</div>
                    </div>
                    <div class="brand-hdivider"></div>
                    <div class="brand-module">Управление заказами</div>
                  </div>

                  <div class="brand-headline">Заказы.<br>В реальном<br>времени.</div>
                  <div class="brand-desc">Единый центр мониторинга и управления заказами. Отслеживайте статусы, управляйте производственными площадками и контролируйте каждую строку.</div>
                </div>

                <div class="brand-bottom">
                  <div class="brand-pills">
                    <span class="pill">Производство</span>
                    <span class="pill">Логистика</span>
                    <span class="pill">Ковров · Новосибирск</span>
                  </div>
                </div>
              </div>

              <!-- ══ Form panel (right) ══ -->
              <div class="panel-form">
                <div class="form-wrap">
                  <div class="form-eyebrow">Система управления</div>
                  <div class="form-title">Добро пожаловать</div>
                  <div class="form-sub">Введите учётные данные для входа</div>

                  {{errHtml}}

                  <form method="post" novalidate>
                    <div class="field">
                      <label class="field-lbl" for="u">Логин</label>
                      <div class="field-wrap">
                        <span class="field-ico">
                          <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16">
                            <path fill-rule="evenodd" d="M10 9a3 3 0 100-6 3 3 0 000 6zm-7 9a7 7 0 1114 0H3z" clip-rule="evenodd"/>
                          </svg>
                        </span>
                        <input id="u" class="inp" name="username" type="text"
                          autocomplete="username" placeholder="Введите логин" autofocus required>
                      </div>
                    </div>
                    <div class="field">
                      <label class="field-lbl" for="p">Пароль</label>
                      <div class="field-wrap">
                        <span class="field-ico">
                          <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16">
                            <path fill-rule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clip-rule="evenodd"/>
                          </svg>
                        </span>
                        <input id="p" class="inp" name="password" type="password"
                          autocomplete="current-password" placeholder="••••••••" required>
                      </div>
                    </div>
                    <button class="btn" type="submit">Войти в систему</button>
                  </form>

                  <div class="form-foot">© 2025 Askona · Система управления заказами</div>
                </div>
              </div>

            </div>
            </body>
            </html>
            """;
        return ctx.Response.WriteAsync(html);
    }
}
