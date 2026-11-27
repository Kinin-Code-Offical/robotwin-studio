using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [lang] The window side of the language packs (Shell/Lang.Core.cs): every element is translated when it is first
    /// laid out. WPF raises Loaded only on elements that listen for it themselves, so the class handler that sees every
    /// element is SizeChanged, which each one gets on its first layout (and again when a text set later resizes it).
    /// </summary>
    public static partial class Lang
    {
        private static bool _installed;

        /// <summary>Translates every element as it is laid out (once, at startup, when a pack is in use).</summary>
        public static void Install()
        {
            if (_installed || !Active) return;
            _installed = true;
            var each = new RoutedEventHandler((s, _) => Translate(s as FrameworkElement));
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.SizeChangedEvent, each, true);
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, each, true);
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((s, _) => TranslateTree(s as DependencyObject)), true);
            EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler((s, _) => TranslateTip(s as ToolTip)), true);
        }

        private static void TranslateTip(ToolTip? tip)
        {
            if (tip != null) Set(tip, ContentControl.ContentProperty);
        }

        private static void TranslateTree(DependencyObject? root)
        {
            if (root == null) return;
            if (root is FrameworkElement fe) Translate(fe);
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is DependencyObject d) TranslateTree(d);
            }
        }

        /// <summary>
        /// The element's texts: a text block's text, a control's string content, a header, a tooltip, a window's title.
        /// They are changed with SetCurrentValue, so a bound text (a tab's title, a list's selected item) keeps its
        /// binding: when its source changes the new text comes through and is translated in turn. A text already in the
        /// pack's language is left alone, so a resize changes nothing.
        /// </summary>
        public static void Translate(FrameworkElement? e)
        {
            if (e == null || !Active || (bool)e.GetValue(KeepProperty)) return;
            switch (e)
            {
                case TextBlock tb:
                    // A block of formatted runs (<TextBlock><Run …/><Bold …/></TextBlock>) has no text of its own.
                    if (DependencyPropertyHelper.GetValueSource(tb, TextBlock.TextProperty).BaseValueSource == BaseValueSource.Default)
                    {
                        Runs(tb);
                        // Made empty, filled by code later ("Prob: …" set on each refresh): if its size never changes,
                        // only a watcher sees the text come.
                        if (tb.Inlines.Count == 0) Follow(tb, TextBlock.TextProperty);
                    }
                    else
                    {
                        Set(tb, TextBlock.TextProperty);
                        Follow(tb, TextBlock.TextProperty);
                    }

                    break;
                case HeaderedItemsControl hi:
                    Set(hi, HeaderedItemsControl.HeaderProperty);
                    break;
                case HeaderedContentControl hc:
                    Set(hc, HeaderedContentControl.HeaderProperty);
                    Set(hc, ContentControl.ContentProperty);
                    break;
                case Window w:
                    Set(w, Window.TitleProperty);
                    break;
                case ContentControl cc:
                    Set(cc, ContentControl.ContentProperty);
                    if (cc.Content is string) Follow(cc, ContentControl.ContentProperty);
                    break;
            }

            Set(e, FrameworkElement.ToolTipProperty);
        }

        /// <summary>
        /// [lang] Texts that are code, not language (a PLC's VAR, a type, an address): set on an element, it and everything
        /// inside it stay as written. Inherited down the tree.
        /// </summary>
        public static readonly DependencyProperty KeepProperty = DependencyProperty.RegisterAttached("Keep", typeof(bool), typeof(Lang),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static void SetKeep(DependencyObject d, bool value) => d.SetValue(KeepProperty, value);

        public static bool GetKeep(DependencyObject d) => (bool)d.GetValue(KeepProperty);

        private static void Set(DependencyObject d, DependencyProperty p)
        {
            if (d.GetValue(p) is string s && T(s) is var t && t != s) d.SetCurrentValue(p, t);
        }

        /// <summary>
        /// Watches a text that code may change later (a guide card's next step, a button that turns from "Duraklat" into
        /// "Devam"): a text that keeps its size never raises SizeChanged again. The watcher is bound to the element's own
        /// property and kept on the element, so it lives and dies with it (no list anywhere holds it).
        /// </summary>
        private static void Follow(FrameworkElement e, DependencyProperty p)
        {
            if (e.GetValue(WatchProperty) != null) return;
            var w = new Watch { Owner = e };
            e.SetValue(WatchProperty, w);
            System.Windows.Data.BindingOperations.SetBinding(w, Watch.ValueProperty, new System.Windows.Data.Binding { Source = e, Path = new PropertyPath(p), Mode = System.Windows.Data.BindingMode.OneWay });
        }

        private static readonly DependencyProperty WatchProperty = DependencyProperty.RegisterAttached("LangWatch", typeof(Watch), typeof(Lang));

        private sealed class Watch : DependencyObject
        {
            public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(object), typeof(Watch),
                new PropertyMetadata(null, (d, _) => Translate(((Watch)d).Owner)));

            public FrameworkElement? Owner;
        }

        private static void Runs(TextBlock tb) => Runs(tb.Inlines);

        private static void Runs(InlineCollection inlines)
        {
            foreach (var inline in inlines.ToList())   // setting a run's text changes the collection
            {
                if (inline is Run run) Set(run, Run.TextProperty);
                else if (inline is Span span) Runs(span.Inlines);   // <Bold>, <Hyperlink> …
            }
        }

        /// <summary>
        /// The texts on screen under <paramref name="root"/> that the pack leaves as they are (to find what a pack still
        /// lacks: --lang-missing). A text the pack produced is not counted, nor one without letters.
        /// </summary>
        public static SortedSet<string> Untranslated(DependencyObject root)
        {
            var produced = new HashSet<string>(Exact.Values, System.StringComparer.Ordinal);
            var found = new SortedSet<string>(System.StringComparer.Ordinal);
            void Check(string? s)
            {
                if (string.IsNullOrWhiteSpace(s) || !s.Any(char.IsLetter) || produced.Contains(s)) return;
                if (T(s) == s) found.Add(s);
            }

            void Walk(DependencyObject d)
            {
                switch (d)
                {
                    case TextBlock tb:
                        Check(tb.Text);
                        break;
                    case HeaderedItemsControl hi:
                        Check(hi.Header as string);
                        break;
                    case HeaderedContentControl hc:
                        Check(hc.Header as string);
                        break;
                    case ContentControl cc:
                        Check(cc.Content as string);
                        break;
                }

                if (d is FrameworkElement fe) Check(fe.ToolTip as string);
                int n = d is System.Windows.Media.Visual || d is System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetChildrenCount(d) : 0;
                for (int i = 0; i < n; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
                // Menus keep their items out of the visual tree until they open.
                if (d is ItemsControl ic && ic is MenuItem or Menu)
                {
                    foreach (var item in ic.Items)
                    {
                        if (item is DependencyObject child && System.Windows.Media.VisualTreeHelper.GetParent(child) == null) Walk(child);
                    }
                }
            }

            Walk(root);
            return found;
        }
    }
}
