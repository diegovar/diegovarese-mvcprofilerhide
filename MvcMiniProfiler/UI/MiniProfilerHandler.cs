 using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Web.Routing;

using MvcMiniProfiler.Helpers;
using System.Text;

namespace MvcMiniProfiler.UI
{
    /// <summary>
    /// Understands how to route and respond to MiniProfiler UI urls.
    /// </summary>
    public class MiniProfilerHandler : IRouteHandler, IHttpHandler
    {
        internal static HtmlString RenderIncludes(MiniProfiler profiler, RenderPosition? position = null, bool? showTrivial = null, bool? showTimeWithChildren = null, int? maxTracesToShow = null)
        {
            const string format =
@"<link rel=""stylesheet"" type=""text/css"" href=""{path}mini-profiler-includes.css?v={version}"">
<script type=""text/javascript"">
    if (!window.jQuery) document.write(unescape(""%3Cscript src='{path}mini-profiler-jquery.1.6.1.js' type='text/javascript'%3E%3C/script%3E""));
    if (window.jQuery && !window.jQuery.tmpl) document.write(unescape(""%3Cscript src='{path}mini-profiler-jquery.tmpl.beta1.js' type='text/javascript'%3E%3C/script%3E""));
</script>
<script type=""text/javascript"" src=""{path}mini-profiler-includes.js?v={version}""></script>
<script type=""text/javascript"">
    jQuery(function() {{
        MiniProfiler.init({{
            ids: {ids},
            path: '{path}',
            version: '{version}',
            renderPosition: '{position}',
            showTrivial: {showTrivial},
            showChildrenTime: {showChildren},
            maxTracesToShow: {maxTracesToShow}
        }});
        $('.profiler-results').css({ position: 'relative' }).wrap('<div class=""profiler_wrapper closed"" style=""position:fixed; left: 0; top: 10px;""></div>');
                    var wrapper = $('.profiler_wrapper')
                    var toggleView = function (animate) {
                        wrapper.animate({ left: wrapper.hasClass('closed') ? 0 : -parseInt(wrapper.outerWidth()) }, animate ? 300 : 0);
                        wrapper.toggleClass('closed');
                    };
                    $('.profiler-results').parent()
                        .append('<div class=""profiler_toggle"" style=""position: fixed; cursor: pointer; top: 0px; left: 0px; background-color: #000; width: 10px; height: 10px;""></div>')
                        .css({ left: '-71px' })
                        .find('.profiler_toggle')
                            .click(function () {
                                toggleView(true);
                            });
    }});
</script>";
            var result = "";

            if (profiler != null)
            {
                // HACK: unviewed ids are added to this list during Storage.Save, but we know we haven't see the current one yet,
                // so go ahead and add it to the end - it's usually the only id, but if there was a redirect somewhere, it'll be there, too
                MiniProfiler.Settings.EnsureStorageStrategy();
                var ids = MiniProfiler.Settings.Storage.GetUnviewedIds(profiler.User);
                ids.Add(profiler.Id);

                result = format.Format(new
                {
                    path = VirtualPathUtility.ToAbsolute(MiniProfiler.Settings.RouteBasePath).EnsureTrailingSlash(),
                    version = MiniProfiler.Settings.Version,
                    ids = ids.ToJson(),
                    position = (position ?? MiniProfiler.Settings.PopupRenderPosition).ToString().ToLower(),
                    showTrivial = showTrivial ?? MiniProfiler.Settings.PopupShowTrivial ? "true" : "false",
                    showChildren = showTimeWithChildren ?? MiniProfiler.Settings.PopupShowTimeWithChildren ? "true" : "false",
                    maxTracesToShow = maxTracesToShow ?? MiniProfiler.Settings.PopupMaxTracesToShow
                });
            }

            return new HtmlString(result);
        }

        internal static void RegisterRoutes()
        {
            var urls = new[] 
            { 
                "mini-profiler-jquery.1.6.1.js",
                "mini-profiler-jquery.tmpl.beta1.js",
                "mini-profiler-includes.js", 
                "mini-profiler-includes.css", 
                "mini-profiler-includes.tmpl", 
                "mini-profiler-results"
            };
            var routes = RouteTable.Routes;
            var handler = new MiniProfilerHandler();
            var prefix = (MiniProfiler.Settings.RouteBasePath ?? "").Replace("~/", "").EnsureTrailingSlash();

            using (routes.GetWriteLock())
            {
                foreach (var url in urls)
                {
                    var route = new Route(prefix + url, handler)
                    {
                        // we have to specify these, so no MVC route helpers will match, e.g. @Html.ActionLink("Home", "Index", "Home")
                        Defaults = new RouteValueDictionary(new { controller = "MiniProfilerHandler", action = "ProcessRequest" })
                    };

                    // put our routes at the beginning, like a boss
                    routes.Insert(0, route);
                }
            }
        }

        /// <summary>
        /// Returns this <see cref="MiniProfilerHandler"/> to handle <paramref name="requestContext"/>.
        /// </summary>
        public IHttpHandler GetHttpHandler(RequestContext requestContext)
        {
            return this; // elegant? I THINK SO.
        }

        /// <summary>
        /// Try to keep everything static so we can easily be reused.
        /// </summary>
        public bool IsReusable
        {
            get { return true; }
        }

        /// <summary>
        /// Returns either includes' css/javascript or results' html.
        /// </summary>
        public void ProcessRequest(HttpContext context)
        {
            string output;
            string path = context.Request.AppRelativeCurrentExecutionFilePath;

            switch (Path.GetFileNameWithoutExtension(path))
            {
                case "mini-profiler-jquery.1.6.1":
                case "mini-profiler-jquery.tmpl.beta1":
                case "mini-profiler-includes":
                    output = Includes(context, path);
                    break;

                case "mini-profiler-results":
                    output = Results(context);
                    break;

                default:
                    output = NotFound(context);
                    break;
            }

            context.Response.Write(output);
        }

        /// <summary>
        /// Handles rendering static content files.
        /// </summary>
        private static string Includes(HttpContext context, string path)
        {
            var response = context.Response;

            switch (Path.GetExtension(path))
            {
                case ".js":
                    response.ContentType = "application/javascript";
                    break;
                case ".css":
                    response.ContentType = "text/css";
                    break;
                case ".tmpl":
                    response.ContentType = "text/x-jquery-tmpl";
                    break;
                default:
                    return NotFound(context);
            }

            var cache = response.Cache;
            cache.SetCacheability(System.Web.HttpCacheability.Public);
            cache.SetExpires(DateTime.Now.AddDays(7));
            cache.SetValidUntilExpires(true);

            var embeddedFile = Path.GetFileName(path).Replace("mini-profiler-", "");
            return GetResource(embeddedFile);
        }

        /// <summary>
        /// Handles rendering a previous MiniProfiler session, identified by its "?id=GUID" on the query.
        /// </summary>
        private static string Results(HttpContext context)
        {
            // when we're rendering as a button/popup in the corner, we'll pass ?popup=1
            // if it's absent, we're rendering results as a full page for sharing
            var isPopup = !string.IsNullOrWhiteSpace(context.Request.QueryString["popup"]);

            // this guid is the MiniProfiler.Id property
            Guid id;
            if (!Guid.TryParse(context.Request.QueryString["id"], out id))
                return isPopup ? NotFound(context) : NotFound(context, "text/plain", "No Guid id specified on the query string");

            MiniProfiler.Settings.EnsureStorageStrategy();
            var profiler = MiniProfiler.Settings.Storage.Load(id);

            if (profiler == null)
                return isPopup ? NotFound(context) : NotFound(context, "text/plain", "No MiniProfiler results found with Id=" + id.ToString());

            // ensure that callers have access to these results
            var authorize = MiniProfiler.Settings.Results_Authorize;
            if (authorize != null && !authorize(context.Request, profiler))
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "text/plain";
                return "Unauthorized";
            }

            return isPopup ? ResultsJson(context, profiler) : ResultsFullPage(context, profiler);
        }

        private static string ResultsJson(HttpContext context, MiniProfiler profiler)
        {
            context.Response.ContentType = "application/json";
            return MiniProfiler.ToJson(profiler);
        }

        private static string ResultsFullPage(HttpContext context, MiniProfiler profiler)
        {
            context.Response.ContentType = "text/html";
            return new StringBuilder()
                .AppendLine("<html><head>")
                .AppendFormat("<title>{0} ({1} ms) - MvcMiniProfiler Results</title>", profiler.Name, profiler.DurationMilliseconds)
                .AppendLine()
                .AppendLine("<script type='text/javascript' src='https://ajax.googleapis.com/ajax/libs/jquery/1.6.1/jquery.min.js'></script>")
                .Append("<script type='text/javascript'> var profiler = ")
                .Append(MiniProfiler.ToJson(profiler))
                .AppendLine(";</script>")
                .Append(RenderIncludes(profiler)) // figure out how to better pass display options
                .AppendLine("</head><body><div class='profiler-result-full'></div></body></html>")
                .ToString();
        }

        private static string GetResource(string filename)
        {
            filename = filename.ToLower();
            string result;

            if (!_ResourceCache.TryGetValue(filename, out result))
            {
                using (var stream = typeof(MiniProfilerHandler).Assembly.GetManifestResourceStream("MvcMiniProfiler.UI." + filename))
                using (var reader = new StreamReader(stream))
                {
                    result = reader.ReadToEnd();
                }

                _ResourceCache[filename] = result;
            }

            return result;
        }

        /// <summary>
        /// Embedded resource contents keyed by filename.
        /// </summary>
        private static readonly Dictionary<string, string> _ResourceCache = new Dictionary<string, string>();

        /// <summary>
        /// Helper method that sets a proper 404 response code.
        /// </summary>
        private static string NotFound(HttpContext context, string contentType = "text/plain", string message = null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = contentType;

            return message;
        }

    }
}
