namespace Cs2Prak.App.Shell;

internal static class PywebviewShim
{
    public static readonly string[] Methods =
        ["minimize", "toggle_maximize", "is_maximized", "hide_to_tray", "show", "quit"];

    public const string DragRegionSelector = ".pywebview-drag-region";

    public static string Script { get; } = BuildScript();

    private static string BuildScript()
    {
        var names = string.Join(", ", Methods.Select(m => "'" + m + "'"));
        return $$"""
        (function () {
          if (window.pywebview) return;

          var seq = 0;
          var pending = {};

          function post(msg) {
            try { window.chrome.webview.postMessage(JSON.stringify(msg)); } catch (e) {}
          }

          function call(name, args) {
            return new Promise(function (resolve) {
              var id = 'c' + (++seq);
              pending[id] = resolve;
              post({ kind: 'api', id: id, name: name, args: args });
            });
          }

          window.chrome.webview.addEventListener('message', function (e) {
            var m = e.data;
            if (typeof m === 'string') { try { m = JSON.parse(m); } catch (err) { return; } }
            if (!m || m.kind !== 'reply') return;
            var cb = pending[m.id];
            if (cb) { delete pending[m.id]; cb(m.value); }
          });

          var api = {};
          [{{names}}].forEach(function (n) {
            api[n] = function () { return call(n, Array.prototype.slice.call(arguments)); };
          });

          window.pywebview = { platform: 'edgechromium', api: api };

          function initDrag() {
            var grabX = 0, grabY = 0;

            function onMouseMove(ev) {
              post({ kind: 'move', x: ev.screenX - grabX, y: ev.screenY - grabY });
            }
            function onMouseUp() {
              window.removeEventListener('mousemove', onMouseMove);
              window.removeEventListener('mouseup', onMouseUp);
            }

            document.body.addEventListener('mousedown', function (ev) {
              var regions = document.querySelectorAll('{{DragRegionSelector}}');
              var node = ev.target;
              while (node && node !== document.body && node !== document.documentElement) {
                if (node.nodeType === 1) {
                  for (var i = 0; i < regions.length; i++) {
                    if (regions[i] !== node) continue;
                    grabX = ev.clientX;
                    grabY = ev.clientY;
                    window.addEventListener('mouseup', onMouseUp);
                    window.addEventListener('mousemove', onMouseMove);
                    return;
                  }
                }
                node = node.parentNode;
              }
            });

            document.body.addEventListener('touchstart', function (e) {
              if (e.touches.length > 1 || e.targetTouches.length > 1) {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
              }
            }, { passive: false });

            window.addEventListener('wheel', function (e) {
              if (e.ctrlKey) e.preventDefault();
            }, { passive: false });

            document.addEventListener('dragstart', function (e) {
              if (e.target.tagName === 'IMG' || e.target.tagName === 'A') e.preventDefault();
            });
          }

          if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', initDrag);
          } else {
            initDrag();
          }

          window.dispatchEvent(new CustomEvent('pywebviewready'));
        })();
        """;
    }
}
