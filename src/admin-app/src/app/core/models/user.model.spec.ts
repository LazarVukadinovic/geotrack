import { User } from './user.model';

describe('User model', () => {
  it('supports admin user data', () => {
    const user: User = {
      id: '1',
      firstName: 'Ada',
      lastName: 'Admin',
      email: 'ada@example.com',
      role: 'Regional Manager',
      region: '',
      memberSince: '',
      bio: '',
      language: 'en-US',
      timezone: 'UTC',
      notifyRequests: true,
      notifyNewReview: true,
    };

    expect(user.role).toBe('Regional Manager');
  });
});
