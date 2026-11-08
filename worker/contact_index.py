"""Ephemeral index of contact display metadata, never message bodies or an on-disk export."""
class ContactIndex:
    def __init__(self, rows):
        self.entries = []
        self.postings = {}
        for username, nick, remark, alias, avatar, modified in rows:
            if not username:
                continue
            fields = tuple(str(value or '').casefold() for value in (username, nick, remark, alias))
            entry = {'id': username, 'name': (remark or nick or alias or username).strip() or username,
                     'wechat': (alias or username).strip() or username}
            if isinstance(avatar, str) and len(avatar) <= 4096 and avatar.startswith(('https://', 'http://')):
                entry['avatarUrl'] = avatar
            timestamp = modified if isinstance(modified, (int, float)) else 0
            index = len(self.entries)
            self.entries.append((entry, fields, timestamp))
            grams = set()
            for field in fields:
                grams.update(field)
                grams.update(field[i:i+2] for i in range(len(field)-1))
            for gram in grams:
                self.postings.setdefault(gram, set()).add(index)

    def search(self, query, limit=60):
        query = (query or '').strip().casefold()
        if not query:
            candidates = range(len(self.entries))
        else:
            grams = {query} if len(query) == 1 else {query[i:i+2] for i in range(len(query)-1)}
            lists = sorted((self.postings.get(gram, set()) for gram in grams), key=len)
            candidates = lists[0].copy()
            for matches in lists[1:]:
                candidates.intersection_update(matches)
        found = []
        for index in candidates:
            entry, fields, modified = self.entries[index]
            if query and not any(query in field for field in fields):
                continue
            rank = 0 if query in fields else 1 if any(field.startswith(query) for field in fields) else 2
            found.append((rank, -modified, entry['name'].casefold(), entry['id'], entry))
        found.sort(key=lambda item: item[:4])
        return [item[4] for item in found[:limit]]
