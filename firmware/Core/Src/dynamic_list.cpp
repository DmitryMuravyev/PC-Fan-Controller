////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// dynamic_list.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <dynamic_list.h>
#include <new>


// Default lock
ILock & DynamicList::defaultLock() {
    static IrqLock lock;
    return lock;
}


// Constructor
DynamicList::DynamicList(ILock & l) : lock(l) {}


// Destructor
DynamicList::~DynamicList() {
    LockGuard guard(lock);
    ListItem * item = rootItem;
    while (item) {
        ListItem* next = item->next;
        delete item;
        item = next;
    }
    gen++;
}


// Internal helpers: caller already holds lock

// Getting an element by its index.
// Either by sequential iteration, or in the case of sequential access,
// the element following the previously requested one is selected.
ListItem * DynamicList::getItemAtIndex(int index, const ListCursor * cursor) const {
	if ((index >= 0) && (index < count)) {
		int i, lastItemIndex = count - 1;
		ListItem * item;

		if (index == lastItemIndex) {
			i = lastItemIndex;
			item = lastItem;
	    } else if (cursor && cursor->item && (cursor->gen == gen) && (index >= cursor->index)) {
	        // Fast path: cursor is still valid and we can start from it
	        i = cursor->index;
	        item = cursor->item;
		} else {
	        // Slow path: cursor is stale or we have no hint
			i = 0;
			item = rootItem;
		}

		while (i < index) {
			item = item->next;
			i++;
		}

		return (item);
	}

	return (nullptr);
}


// Deleting an element with maintaining the list structure
void DynamicList::deleteItem(ListItem * itemBefore, ListItem * itemToDelete, releaseObject relDelegate) {
	ListItem * itemAfter = itemToDelete->next;
	if (itemBefore) {
		itemBefore->next = itemAfter;
	} else {
		rootItem = itemAfter;
	}

	if (itemAfter == nullptr) lastItem = itemBefore;

	if (relDelegate) relDelegate(itemToDelete->object);
	delete(itemToDelete);
	count--;
}


// Public API

// Get the first object of the list
void * DynamicList::getFirst(ListCursor * cursor) const {
    LockGuard guard(lock);
	if (cursor == nullptr) cursor = &internalCursor;
    cursor->item = rootItem;
    cursor->index = rootItem ? 0 : -1;
    cursor->gen = gen;
    return rootItem ? rootItem->object : nullptr;
}


// Get the last object of the list
void * DynamicList::getLast(ListCursor * cursor) const {
    LockGuard guard(lock);
	if (cursor == nullptr) cursor = &internalCursor;
    cursor->item = lastItem;
    cursor->index = lastItem ? (count - 1) : -1;
    cursor->gen = gen;
    return lastItem ? lastItem->object : nullptr;
}


// Get the next object following the previously requested one
void * DynamicList::getNext(ListCursor * cursor) const {
    LockGuard guard(lock);
	if (cursor == nullptr) cursor = &internalCursor;

	ListItem * next;
	if (cursor->gen != gen) {
		// The list has been changed - restart from the first element
		// (without calling getFirst(), so as not to acquire the lock twice).
		next = rootItem;
		cursor->index = -1;
		cursor->gen = gen;
	} else {
		// The end of the list has already been reached
		if (cursor->item == nullptr) return nullptr;
		next = cursor->item->next;
	}

    cursor->item = next;

    if (next == nullptr) {
        cursor->index = -1;
        return nullptr;
    }

    cursor->index++;
    return next->object;
}


// Get an object with the specified index
void * DynamicList::getObjectAtIndex(int index, ListCursor * cursor) const {
    LockGuard guard(lock);
	if (cursor == nullptr) cursor = &internalCursor;
	ListItem * item = getItemAtIndex(index, cursor);
    if (item) {
        cursor->item = item;
        cursor->index = index;
        cursor->gen = gen;
        return item->object;
    }
    cursor->item = nullptr;
    cursor->index = -1;
    return nullptr;
}


// Stateless: no cursor, no cached pointer. Safe for ad-hoc ISR reads.
void* DynamicList::getObjectAtIndexSafe(int index) const {
    LockGuard guard(lock);
    if (index < 0 || index >= count) return nullptr;

	ListItem * item = getItemAtIndex(index);
    return item ? item->object : nullptr;
}


// Get the position of an object in the list
int DynamicList::getIndexOfObject(void * object) {
    if (object) {
		LockGuard guard(lock);
		ListItem * item = rootItem;
		int index = 0;
		while (item && (item->object != object)) {
			item = item->next;
			index++;
		}
		if (index < count) return (index);
    }
	return -1;
}


// Add an object to the end of the list
bool DynamicList::addObject(void * newObject) {
    if (!newObject) return false;
    ListItem * newItem = new (std::nothrow) ListItem;
    if (!newItem) return false;

	newItem->object = newObject;
	newItem->next = nullptr;

    LockGuard guard(lock);

	if (lastItem) {
		lastItem->next = newItem;
	} else {
		rootItem = newItem;
	}

	lastItem = newItem;
	count++;
	gen++;
	return (true);
}


// Insert an object into the list at the specified index
bool DynamicList::insertAtIndex(void * newObject, int index) {
    if (!newObject) return false;

    LockGuard guard(lock);
    if (index < 0 || index > count) return false;
	ListItem * newItem = new (std::nothrow) ListItem;
    if (!newItem) return false;


	ListItem * itemBefore;
	ListItem * itemAfter;

	if (index == 0) {
		itemBefore = nullptr;
		itemAfter = rootItem;
	} else if (index == count) {
		itemBefore = lastItem;
		itemAfter = nullptr;
	} else {
		itemBefore = getItemAtIndex(index - 1);
		itemAfter = itemBefore->next;
	}

	newItem->object = newObject;
	newItem->next = itemAfter;

    if (itemBefore) {
        itemBefore->next = newItem;
    } else {
        rootItem = newItem;
    }
    if (!itemAfter) lastItem = newItem;

    count++;
    gen++;

	return (true);
}


// Delete an object with the specified index.
// It is possible to call a delegate to release an object (if passed).
void DynamicList::deleteAtIndex(int index, releaseObject relDelegate) {
    LockGuard guard(lock);
    if (index < 0 || index >= count) return;

	ListItem * itemBefore;
	ListItem * item;

	if (index == 0) {
		itemBefore = nullptr;
		item = rootItem;
	} else {
		itemBefore = getItemAtIndex(index - 1);
		item = itemBefore->next;
	}

	deleteItem(itemBefore, item, relDelegate);
	gen++;
}


// Delete specified object from the list.
// It is possible to call a delegate to release an object (if passed).
void DynamicList::deleteObject(void * object, releaseObject relDelegate) {
    if (!object) return;
    LockGuard guard(lock);

	ListItem * itemBefore = nullptr;
	ListItem * item = rootItem;
	int index = 0;

	while (item && (item->object != object)) {
		itemBefore = item;
		item = item->next;
		index++;
	}
	if (item) {
		deleteItem(itemBefore, item, relDelegate);
		gen++;
	}
}


// Sort the list using a delegate to compare objects
void DynamicList::sort(objectsShouldBeSwapped cmpDelegate) {
    if (!cmpDelegate) return;
    LockGuard guard(lock);
    if (count <= 1) return;

	ListItem * item;
	ListItem * nextItem;
	int index, lastIndex = count - 1;
	bool sortingInProgress;

	do {
		sortingInProgress = false;
		item = rootItem;
		index = 0;
		while (index < lastIndex) {
			nextItem = item->next;
			void * currentObject = item->object;
			void * nextObject = nextItem->object;
			if (cmpDelegate(currentObject, nextObject)) {
				item->object = nextObject;
				nextItem->object = currentObject;
				sortingInProgress = true;
			}
			item = nextItem;
			index++;
		}
		lastIndex--;
	} while (sortingInProgress);

	gen++;
}

// END-OF-FILE
