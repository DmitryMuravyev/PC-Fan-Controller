////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// dynamic_list.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	dynamic_list.h
 * @brief	A simple unidirectional dynamic list with fast sequential access,
 * 			random access and sorting capabilities.
 */

#ifndef _DYNAMIC_LIST_
#define _DYNAMIC_LIST_


// Includes
#include <stdint.h>
#include <cstddef>
#include "ilock.h"
#include "irq_lock.h"


// Types

/**
 * @brief	List element.
 */
typedef struct listItem {
	void * object;							///< Stored object
	listItem * next;						///< Next list element (nullptr - the last one)
} ListItem;


/**
 * @brief	Objects comparison delegate (used for sorting).
 * @param	object1			The first object.
 * @param	object2			The second object (following the first one).
 * @retval	true if the objects must be swapped.
 */
typedef bool (*objectsShouldBeSwapped) (void * object1, void * object2);

/**
 * @brief	Object release delegate (called when the object is deleted from the list).
 * @param	object			The object being deleted.
 */
typedef void (*releaseObject) (void * object);


/**
 * @brief	External cursor for sequential iteration.
 * @note	Each independent caller must use its own instance.
 */
struct ListCursor {
    ListItem * item = nullptr;				///< Current list element
    int index = -1;							///< Index of the current list element (-1 - none)
    uint32_t gen = 0;						///< List generation when the cursor was last valid
};


/**
 * @brief	Dynamic list of objects (pointers).
 * @note	The list doesn't own the objects: they must be released by the delegate
 * 			passed to the deletion functions. All the public functions are internally locked
 * 			by the lock object passed to the constructor.
 */
class DynamicList {

private:

    ILock & lock;							///< Lock protecting the list structure
    uint32_t gen = 0;						///< Incremented on every structural mutation. Invalidates all cursors.

    /**
     * @brief	Get the default lock (masks all interrupts).
     * @retval	Lock object.
     */
    static ILock & defaultLock();

    mutable ListCursor internalCursor;		///< Cursor used if no external cursor is passed

protected:

	volatile uint16_t count = 0;			///< Number of elements

	ListItem * rootItem = nullptr;			///< First list element
	ListItem * lastItem = nullptr;			///< Last list element

	/**
	 * @brief	Get the list element by its index (either by sequential iteration,
	 * 			or starting from the cursor position in the case of sequential access).
	 * @note	The caller must already hold the lock.
	 * @param	index			Element index.
	 * @param	cursor			Cursor to start from (optional).
	 * @retval	List element, nullptr if the index is out of range.
	 */
	ListItem * getItemAtIndex(int index, const ListCursor * cursor = nullptr) const;

	/**
	 * @brief	Delete the element with maintaining the list structure.
	 * @note	The caller must already hold the lock.
	 * @param	itemBefore		Previous element (nullptr - the element is the first one).
	 * @param	itemToDelete	Element to delete.
	 * @param	relDelegate		Object release delegate (optional).
	 */
	void deleteItem(ListItem * itemBefore, ListItem * itemToDelete, releaseObject relDelegate);


public:

	/**
	 * @brief	Constructor.
	 * @param	lock			Lock protecting the list. The default lock masks all interrupts
	 * 							(safe, simplest option on M0). We can inject IrqLineLock, PendSvLock,
	 * 							or some Mutex-based lock, if we need something more selective
	 * 							or want to move to FreeRTOS.
	 */
	explicit DynamicList(ILock & lock = defaultLock());

	/**
	 * @brief	Destructor. Deletes the list elements (but not the objects).
	 */
	virtual ~DynamicList();

	// Cursor-based, reentrant API: safe to call concurrently from main
	// and an ISR, as long as each context uses its own ListCursor.
	// A cursor may safely be reused across multiple calls WITHIN the
	// same ISR invocation (nothing else can run meanwhile). If the list
	// is mutated in between, the cursor is invalidated (by the generation
	// counter) and the search restarts from the beginning.

	/**
	 * @brief	Get the first object of the list.
	 * @param	cursor			Cursor to set (optional, nullptr - the internal one).
	 * @retval	Object, nullptr if the list is empty.
	 */
	void * getFirst(ListCursor * cursor = nullptr) const;

	/**
	 * @brief	Get the last object of the list.
	 * @param	cursor			Cursor to set (optional, nullptr - the internal one).
	 * @retval	Object, nullptr if the list is empty.
	 */
	void * getLast(ListCursor * cursor = nullptr) const;

	/**
	 * @brief	Get the object following the previously requested one.
	 * @note	If the list has been changed since the cursor was set, the first object is returned.
	 * @param	cursor			Cursor (optional, nullptr - the internal one).
	 * @retval	Object, nullptr if the end of the list is reached.
	 */
	void * getNext(ListCursor * cursor = nullptr) const;

	/**
	 * @brief	Get the object with the specified index.
	 * @param	index			Object index.
	 * @param	cursor			Cursor used to speed up the sequential access (optional, nullptr - the internal one).
	 * @retval	Object, nullptr if the index is out of range.
	 */
	void * getObjectAtIndex(int index, ListCursor * cursor = nullptr) const;

	/**
	 * @brief	Get the object with the specified index (stateless: no cursor is used or kept,
	 * 			so it can be freely interleaved with anything else).
	 * @param	index			Object index.
	 * @retval	Object, nullptr if the index is out of range.
	 */
	void * getObjectAtIndexSafe(int index) const;

	/**
	 * @brief	Get the position of the object in the list.
	 * @param	object			Object to find.
	 * @retval	Object index, -1 if the object isn't found.
	 */
	int getIndexOfObject(void * object);


	// Mutating API - always internally locked.

	/**
	 * @brief	Add the object to the end of the list.
	 * @param	newObject		Object to add (must not be nullptr).
	 * @retval	true if the object is added, false if there is no memory for a new element.
	 */
	bool addObject(void * newObject);

	/**
	 * @brief	Insert the object into the list at the specified index.
	 * @param	newObject		Object to insert (must not be nullptr).
	 * @param	index			Position (0 - the beginning, length() - the end).
	 * @retval	true if the object is inserted.
	 */
	bool insertAtIndex(void * newObject, int index);

	/**
	 * @brief	Delete the object with the specified index.
	 * @param	index			Object index.
	 * @param	relDelegate		Object release delegate (optional).
	 */
	void deleteAtIndex(int index, releaseObject relDelegate);

	/**
	 * @brief	Delete the specified object from the list.
	 * @param	object			Object to delete.
	 * @param	relDelegate		Object release delegate (optional).
	 */
	void deleteObject(void * object, releaseObject relDelegate);

	/**
	 * @brief	Sort the list (bubble sort, the objects are swapped between the elements).
	 * @param	cmpDelegate		Objects comparison delegate.
	 */
	void sort(objectsShouldBeSwapped cmpDelegate);

	/**
	 * @brief	Get the number of objects in the list.
	 * @retval	Number of objects.
	 */
	uint16_t length() const { return count; }

};

#endif /* _DYNAMIC_LIST_ */

// END-OF-FILE
