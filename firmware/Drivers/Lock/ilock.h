////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// ilock.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	ilock.h
 * @brief	An abstract lock class so shared data structures (like DynamicList)
 * 			don't need to know whether they run on bare metal or under FreeRTOS.
 */

#ifndef _ILOCK_H_
#define _ILOCK_H_


// Includes
#include <stdint.h>
#include "stm32f0xx.h"   // brings in core_cm0.h -> __get_IPSR/__disable_irq/etc.


/**
 * @brief	Lock interface.
 */
class ILock {

public:

	/**
	 * @brief	Acquire the lock.
	 */
    virtual void lock() = 0;

	/**
	 * @brief	Release the lock.
	 */
    virtual void unlock() = 0;

    virtual ~ILock() = default;

};


/**
 * @brief	RAII helper: acquires the lock in the constructor and releases it in the destructor.
 * @note	Always prefer this over manual lock()/unlock() pairs.
 */
class LockGuard {

private:

    ILock& lock_;							///< Guarded lock


public:

	/**
	 * @brief	Constructor. Acquires the lock.
	 * @param	lock			Lock to acquire.
	 */
    explicit LockGuard(ILock& lock) : lock_(lock) { lock_.lock(); }

	/**
	 * @brief	Destructor. Releases the lock.
	 */
    ~LockGuard() { lock_.unlock(); }

    LockGuard(const LockGuard&) = delete;
    LockGuard& operator=(const LockGuard&) = delete;

};


#endif /* _ILOCK_H_ */

// END-OF-FILE
