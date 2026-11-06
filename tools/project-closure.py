"""Print a project and every project it references (not analyzers or source generators), dependencies first."""
import os
import sys
import xml.etree.ElementTree as ET

order, seen = [], set()


def visit(path):
    path = os.path.normpath(path)
    if path in seen:
        return
    seen.add(path)
    for ref in ET.parse(path).getroot().iter('ProjectReference'):
        meta = {child.tag: (child.text or '').strip() for child in ref}
        meta.update(ref.attrib)
        if meta.get('ReferenceOutputAssembly', '').lower() == 'false' or meta.get('OutputItemType') == 'Analyzer':
            continue
        visit(os.path.join(os.path.dirname(path), ref.attrib['Include'].replace('\\', '/')))
    order.append(path)


visit(sys.argv[1])
print('\n'.join(order))
