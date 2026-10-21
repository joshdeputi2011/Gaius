import 'package:flutter/material.dart';
import 'package:fluentui_system_icons/fluentui_system_icons.dart';

import 'surfaces.dart';

const officeTabs = <String, List<String>>{
  'document': [
    'Home',
    'Insert',
    'Design',
    'Layout',
    'References',
    'Review',
    'View',
  ],
  'spreadsheet': [
    'Home',
    'Insert',
    'Page Layout',
    'Formulas',
    'Data',
    'Review',
    'View',
  ],
  'presentation': [
    'Home',
    'Insert',
    'Design',
    'Transitions',
    'Animations',
    'Slide Show',
    'Review',
    'View',
  ],
  'pdf': ['Home', 'Organize', 'Edit', 'Convert', 'Compress', 'View'],
  'image': [
    'Home',
    'Transform',
    'Adjust',
    'Annotate',
    'Compress',
    'Convert',
    'View',
  ],
};

class OfficeActions extends InheritedWidget {
  const OfficeActions({
    super.key,
    required this.kind,
    required this.saveAs,
    required this.share,
    required this.printPreview,
    required this.search,
    required super.child,
  });
  final String kind;
  final VoidCallback saveAs, share, printPreview, search;
  static OfficeActions? of(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<OfficeActions>();
  @override
  bool updateShouldNotify(OfficeActions oldWidget) => true;
}

class OfficeRibbon extends StatefulWidget {
  const OfficeRibbon({super.key, required this.children, this.tabs = const {}});
  final List<Widget> children;
  final Map<String, List<Widget>> tabs;
  @override
  State<OfficeRibbon> createState() => _OfficeRibbonState();
}

class _OfficeRibbonState extends State<OfficeRibbon> {
  String selected = 'Home';
  bool collapsed = false;
  @override
  Widget build(BuildContext context) {
    final actions = OfficeActions.of(context);
    final names = ['File', ...officeTabs[actions?.kind ?? 'document']!];
    final file = <Widget>[
      RibbonCommand(
        icon: FluentIcons.save_24_regular,
        label: 'Save as',
        onPressed: actions?.saveAs,
      ),
      RibbonCommand(
        icon: FluentIcons.share_24_regular,
        label: 'Share',
        onPressed: actions?.share,
      ),
      RibbonCommand(
        icon: FluentIcons.print_24_regular,
        label: 'Print preview',
        onPressed: actions?.printPreview,
      ),
      RibbonCommand(
        icon: FluentIcons.search_24_regular,
        label: 'Search',
        onPressed: actions?.search,
      ),
    ];
    final commands = selected == 'File'
        ? file
        : selected == 'Home'
        ? widget.children
        : widget.tabs[selected] ?? widget.children;
    return GlassSurface(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            children: [
              Expanded(
                child: SizedBox(
                  height: 43,
                  child: ListView(
                    scrollDirection: Axis.horizontal,
                    padding: const EdgeInsets.symmetric(horizontal: 6),
                    children: [
                      for (final name in names)
                        Padding(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 2,
                            vertical: 4,
                          ),
                          child: Semantics(
                            selected: selected == name,
                            button: true,
                            child: TextButton(
                              style: TextButton.styleFrom(
                                backgroundColor: selected == name
                                    ? const Color(0xffdce8f8)
                                    : Colors.transparent,
                                foregroundColor: selected == name
                                    ? officeBlue
                                    : const Color(0xff53667f),
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 14,
                                ),
                                minimumSize: const Size(52, 36),
                                shape: RoundedRectangleBorder(
                                  borderRadius: BorderRadius.circular(9),
                                ),
                              ),
                              onPressed: () => setState(() {
                                selected = name;
                                collapsed = false;
                              }),
                              child: Text(
                                name,
                                style: TextStyle(
                                  fontSize: 12,
                                  fontWeight: selected == name
                                      ? FontWeight.w600
                                      : FontWeight.w400,
                                ),
                              ),
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ),
              IconButton(
                tooltip: collapsed ? 'Expand ribbon' : 'Collapse ribbon',
                onPressed: () => setState(() => collapsed = !collapsed),
                icon: Icon(
                  collapsed
                      ? FluentIcons.chevron_down_24_regular
                      : FluentIcons.chevron_up_24_regular,
                  size: 18,
                ),
              ),
            ],
          ),
          if (!collapsed)
            SizedBox(
              height: MediaQuery.sizeOf(context).width >= 840 ? 86 : 78,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 8),
                children: commands,
              ),
            ),
        ],
      ),
    );
  }
}

Future<String?> askText(
  BuildContext context,
  String title, {
  String initial = '',
  String? hint,
}) async {
  final controller = TextEditingController(text: initial);
  final value = await showDialog<String>(
    context: context,
    builder: (c) => AlertDialog(
      title: Text(title),
      content: TextField(
        controller: controller,
        autofocus: true,
        decoration: InputDecoration(hintText: hint),
        onSubmitted: (value) => Navigator.pop(c, value),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(c),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: () => Navigator.pop(c, controller.text),
          child: const Text('Apply'),
        ),
      ],
    ),
  );
  // The route can animate after completion; dispose after the field detaches.
  Future<void>.delayed(const Duration(milliseconds: 350), controller.dispose);
  return value;
}
