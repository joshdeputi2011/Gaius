import 'package:flutter/material.dart';
import 'package:fluentui_system_icons/fluentui_system_icons.dart';

import 'surfaces.dart';

const officeTabs = <String, List<String>>{
  'document': [
    'Home',
    'Insert',
    'Draw',
    'Design',
    'Layout',
    'References',
    'Review',
    'View',
  ],
  'spreadsheet': [
    'Home',
    'Insert',
    'Draw',
    'Page Layout',
    'Formulas',
    'Data',
    'Review',
    'View',
  ],
  'presentation': [
    'Home',
    'Insert',
    'Draw',
    'Design',
    'Transitions',
    'Animations',
    'Slide Show',
    'Review',
    'View',
  ],
  'pdf': ['Home', 'Draw', 'Organize', 'Edit', 'Convert', 'Compress', 'View'],
  'image': [
    'Home',
    'Draw',
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
    this.draw,
    required super.child,
  });
  final String kind;
  final VoidCallback saveAs, share, printPreview, search;
  final VoidCallback? draw;
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
    final names = officeTabs[actions?.kind ?? 'document']!;
    final commands = selected == 'Home'
        ? widget.children
        : widget.tabs[selected] ??
              (selected == 'Draw'
                  ? [
                      RibbonCommand(
                        label: 'Handwriting',
                        icon: FluentIcons.draw_shape_24_regular,
                        onPressed: actions?.draw,
                      ),
                    ]
                  : widget.children);
    return Padding(
      padding: const EdgeInsets.fromLTRB(8, 4, 8, 8),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(16),
        child: SoftSurface(
          radius: 16,
          child: GlassSurface(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Row(
                  children: [
                    PopupMenuButton<String>(
                      tooltip: 'File',
                      position: PopupMenuPosition.under,
                      color: Theme.of(context).colorScheme.surface,
                      elevation: 8,
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(14),
                      ),
                      onSelected: (value) {
                        switch (value) {
                          case 'Save as':
                            actions?.saveAs();
                          case 'Share':
                            actions?.share();
                          case 'Print preview':
                            actions?.printPreview();
                          case 'Search':
                            actions?.search();
                        }
                      },
                      itemBuilder: (_) => [
                        for (final label in [
                          'Save as',
                          'Share',
                          'Print preview',
                          'Search',
                        ])
                          PopupMenuItem(value: label, child: Text(label)),
                      ],
                      child: const Padding(
                        padding: EdgeInsets.symmetric(
                          horizontal: 14,
                          vertical: 12,
                        ),
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Text('File', style: TextStyle(fontSize: 12)),
                            SizedBox(width: 5),
                            Icon(Icons.keyboard_arrow_down, size: 15),
                          ],
                        ),
                      ),
                    ),
                    Expanded(
                      child: SizedBox(
                        height: 43,
                        child: DefaultTabController(
                          length: names.length,
                          child: TabBar(
                            isScrollable: true,
                            tabAlignment: TabAlignment.start,
                            dividerColor: Colors.transparent,
                            indicatorSize: TabBarIndicatorSize.tab,
                            indicatorPadding: const EdgeInsets.symmetric(
                              vertical: 5,
                            ),
                            indicator: BoxDecoration(
                              color: officeBlue,
                              borderRadius: BorderRadius.circular(24),
                            ),
                            labelColor: Colors.white,
                            unselectedLabelColor: Theme.of(context)
                                .colorScheme
                                .onSurfaceVariant,
                            labelStyle: const TextStyle(
                              fontSize: 12,
                              fontWeight: FontWeight.w500,
                            ),
                            labelPadding: const EdgeInsets.symmetric(
                              horizontal: 16,
                            ),
                            onTap: (index) => setState(() {
                              selected = names[index];
                              collapsed = false;
                            }),
                            tabs: [for (final name in names) Tab(text: name)],
                          ),
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
          ),
        ),
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
